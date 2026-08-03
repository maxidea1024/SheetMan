using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SheetMan.Exporters;
using SheetMan.Recipe;

namespace SheetMan.History
{
    /// <summary>
    /// The history, over HTTP: a JSON API and the page that draws it.
    ///
    /// Every endpoint calls <see cref="HistoryQuery"/> and serialises what it returns with
    /// the same serialiser the command line uses. That is not tidiness - it is the reason a
    /// number on the page cannot disagree with the same number from `--history`, and a test
    /// compares the two byte for byte.
    ///
    /// Read-only, throughout. Nothing here writes, and the account it connects with need
    /// not be able to; only a conversion adds to the history. A server that could modify
    /// what it serves is a server that can corrupt it.
    /// </summary>
    internal static class HistoryServer
    {
        /// <summary>Where the token is read from, when one is needed.</summary>
        public const string TokenVariable = "SHEETMAN_SERVE_TOKEN";

        private const string ApiPrefix = "/api/v1";

        public static int Run(Options options, RecipeModel recipe)
        {
            var (connectionString, projectKey) = HistoryCommand.Connection(options, recipe);

            string bind = string.IsNullOrWhiteSpace(options.Bind) ? "127.0.0.1" : options.Bind.Trim();
            int port = options.Port <= 0 ? 8080 : options.Port;

            string token = Environment.GetEnvironmentVariable(TokenVariable);

            RefuseUnprotectedExposure(bind, token);

            var builder = WebApplication.CreateSlimBuilder();

            // Kestrel's own logging duplicates what Serilog already reports, in a different
            // shape. One log.
            builder.Logging.ClearProviders();

            // Configured on Kestrel rather than through a URL string: an address that does
            // not parse should be reported here rather than accepted and silently turned
            // into a listener on something else.
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(Address(bind), port));

            var app = builder.Build();

            Map(app, connectionString, projectKey, token);

            Log.Information($"Serving the history of `{projectKey}` on http://{bind}:{port}/");

            if (token != null)
                Log.Information($"A bearer token is required; it comes from ${TokenVariable}.");

            app.Run();

            return 0;
        }

        /// <summary>
        /// The address to listen on.
        ///
        /// `localhost` and `*` are spelled the way people expect rather than as the numbers
        /// Kestrel wants; anything else has to be an address, and is rejected here rather
        /// than turning into a listener somewhere unintended.
        /// </summary>
        private static System.Net.IPAddress Address(string bind)
        {
            if (string.Equals(bind, "localhost", StringComparison.OrdinalIgnoreCase))
                return System.Net.IPAddress.Loopback;

            if (bind == "*" || bind == "0.0.0.0")
                return System.Net.IPAddress.Any;

            if (System.Net.IPAddress.TryParse(bind, out var address))
                return address;

            throw new SheetManException(
                $"--bind {bind} is not an address. Use an IP, `localhost`, or `0.0.0.0` for every " +
                $"interface.");
        }

        /// <summary>
        /// Refuses to listen beyond this machine without a token.
        ///
        /// Opening a port and forgetting the authentication is the ordinary way a tool like
        /// this leaks, and what leaks here is every value in the project's design data plus
        /// the name of everyone who touched it. Loopback needs no token; anything else does,
        /// and the refusal is up front rather than a warning nobody reads.
        /// </summary>
        private static void RefuseUnprotectedExposure(string bind, string token)
        {
            if (!string.IsNullOrEmpty(token))
                return;

            bool loopback = bind == "127.0.0.1" || bind == "::1" || bind == "localhost";

            if (loopback)
                return;

            throw new SheetManException(
                $"--bind {bind} would serve the history to anything that can reach this machine, " +
                $"and no token is set. Set ${TokenVariable} to a secret and send it as " +
                $"`Authorization: Bearer <token>`, or leave --bind at 127.0.0.1.");
        }

        // ----------------------------------------------------------------- routes

        private static void Map(WebApplication app, string connectionString, string project, string token)
        {
            if (token != null)
                app.Use((context, next) => Authorize(context, token, next));

            app.MapGet("/", () => Html(HistoryView.Live()));

            app.MapGet("/history.css", () => Asset("history.css"));
            app.MapGet("/history.js", () => Asset("history.js"));

            // Says whether this process is up. Deliberately does not touch the database:
            // a load balancer restarting the server because MySQL blinked would take the
            // one thing that could have explained the outage off the air with it.
            app.MapGet(ApiPrefix + "/healthz", () => Results.Text("ok", "text/plain; charset=utf-8"));

            Query(app, "/projects", (q, r, _) => q.Projects());
            Query(app, "/branches", (q, r, p) => q.Branches(p));
            Query(app, "/tables", (q, r, p) => q.Tables(p, Branch(q, r, p)));

            Query(app, "/snapshots", (q, r, p) => q.Snapshots(p, Branch(q, r, p), Int(r, "limit", 100)));

            Query(app, "/stats", (q, r, p) => q.Stats(p, Branch(q, r, p), Str(r, "at")));

            Query(app, "/trend", (q, r, p) => q.Trend(
                p, Branch(q, r, p), Str(r, "metric") ?? "rows", Str(r, "table"), Int(r, "limit", 500)));

            Query(app, "/authors", (q, r, p) => q.Authors(
                p, Branch(q, r, p), Str(r, "from"), Str(r, "to")));

            Query(app, "/cell", (q, r, p) => q.CellHistory(
                p, Branch(q, r, p), Str(r, "table"), Str(r, "row"), Str(r, "field"), Int(r, "limit", 200)));

            Query(app, "/diff", (q, r, p) => q.Diff(
                p, Branch(q, r, p), Str(r, "from"), Str(r, "to"),
                Str(r, "table"), Str(r, "field"), Str(r, "author"),
                Int(r, "limit", HistoryQuery.DefaultLimit)));

            Query(app, "/dashboard", (q, r, p) => q.Dashboard(
                p, Branch(q, r, p), Str(r, "from"), Str(r, "to"),
                Str(r, "table"), Str(r, "field"), Str(r, "author"),
                Int(r, "limit", HistoryQuery.DefaultLimit)));

            void Query(WebApplication host, string path, Func<HistoryQuery, HttpRequest, string, object> answer)
            {
                host.MapGet(ApiPrefix + path, (HttpContext context) =>
                {
                    // A connection per request. HistoryQuery holds one and MySQL connections
                    // are not concurrent; the pool makes this cheap.
                    using var query = HistoryQuery.Open(connectionString);

                    string asked = Str(context.Request, "project") ?? project;

                    return Json(context, HistoryCommand.Serialize(answer(query, context.Request, asked)));
                });
            }
        }

        private static async Task Authorize(HttpContext context, string token, Func<Task> next)
        {
            // The page and its assets are behind the token too. They carry no data, but a
            // reachable page invites somebody to conclude the port is open to them.
            string header = context.Request.Headers.Authorization.ToString();

            string presented = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header.Substring("Bearer ".Length).Trim()
                : context.Request.Query["token"].ToString();

            if (!FixedTimeEquals(presented, token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";

                await context.Response.WriteAsync("A bearer token is required.");
                return;
            }

            await next();
        }

        /// <summary>
        /// Compares in time that does not depend on how much of the token matched.
        ///
        /// A plain comparison returns sooner the earlier it finds a difference, which over
        /// enough attempts tells an attacker the token one character at a time.
        /// </summary>
        private static bool FixedTimeEquals(string presented, string expected)
        {
            if (string.IsNullOrEmpty(presented))
                return false;

            return CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        }

        // -------------------------------------------------------------- responses

        /// <summary>
        /// A JSON answer, with an entity tag.
        ///
        /// Snapshots never change once written, so an answer about a closed range is good
        /// for ever - and the ranges a page asks about again and again are exactly those.
        /// </summary>
        private static IResult Json(HttpContext context, string body)
        {
            string tag = "\"" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(body))).Substring(0, 32).ToLowerInvariant() + "\"";

            if (context.Request.Headers.IfNoneMatch.ToString() == tag)
                return Results.StatusCode(StatusCodes.Status304NotModified);

            context.Response.Headers.ETag = tag;

            return Results.Text(body, "application/json; charset=utf-8");
        }

        private static IResult Html(string body) => Results.Text(body, "text/html; charset=utf-8");

        private static IResult Asset(string name)
            => Results.Text(HistoryView.Asset(name), HistoryView.ContentTypeOf(name));

        // ---------------------------------------------------------------- reading

        private static string Str(HttpRequest request, string name)
        {
            string value = request.Query[name].ToString();

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static int Int(HttpRequest request, string name, int fallback)
        {
            string value = Str(request, name);

            if (value == null)
                return fallback;

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw new SheetManException($"`{name}={value}` is not a number.");

            return parsed;
        }

        private static string Branch(HistoryQuery query, HttpRequest request, string project)
            => Str(request, "branch") ?? query.DefaultBranch(project);
    }
}
