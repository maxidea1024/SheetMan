using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SheetMan.CodeGeneration;
using Xunit;

using ValueType = SheetMan.Models.ValueType;

namespace SheetMan.Tests
{
    /// <summary>
    /// Every output language against every type a sheet can hold.
    ///
    /// The thirteen generators each carry their own switch over <see cref="ValueType"/>, and
    /// each ends in a `default:` that throws. That is the right thing at the moment a
    /// conversion asks for a type the generator cannot render - but it means adding a type to
    /// the enum leaves thirteen places that compile perfectly and fail only when somebody's
    /// sheet uses it, one language at a time, in whatever order they are unlucky in.
    ///
    /// So the question is asked here once, of the table the generators read. A new
    /// <see cref="ValueType"/> fails this the moment it is added, naming the languages that
    /// have not been taught it, rather than reaching a user first.
    ///
    /// The profiles are found by reflection rather than listed, because a list is the other
    /// thing somebody forgets: a fourteenth language whose profile nothing here mentions
    /// would be exactly as unchecked as the types are now.
    /// </summary>
    public class LanguageProfileTests
    {
        /// <summary>
        /// Every profile <see cref="LanguageProfile"/> declares.
        /// </summary>
        private static IReadOnlyList<LanguageProfile> Profiles()
            => typeof(LanguageProfile)
               .GetFields(BindingFlags.Public | BindingFlags.Static)
               .Where(field => field.FieldType == typeof(LanguageProfile))
               .Select(field => (LanguageProfile)field.GetValue(null))
               .ToList();

        /// <summary>
        /// The scalar types a generator has to be able to name.
        ///
        /// `None` and `Unresolved` are not types a field ends up holding - one is unset and
        /// the other is a reference before resolution ran. `Enum` and `ForeignRecord` are
        /// deliberately absent from the profiles: both name something declared in the sheets,
        /// and each language qualifies that its own way, so those two arms stay in the
        /// generators. Everything else is here.
        /// </summary>
        private static IEnumerable<ValueType> RenderableScalars()
            => Enum.GetValues<ValueType>()
                   .Where(type => type == SheetMan.Models.ValueTypes.ElementOf(type))
                   .Where(type => type != ValueType.None
                               && type != ValueType.Unresolved
                               && type != ValueType.Enum
                               && type != ValueType.ForeignRecord);

        [Fact]
        public void The_profiles_are_all_found()
        {
            var ids = Profiles().Select(profile => profile.Id).OrderBy(id => id, StringComparer.Ordinal);

            // Named rather than counted, so adding a language is a deliberate edit here and
            // removing one cannot pass by accident.
            Assert.Equal(
                new[]
                {
                    "c", "cpp", "csharp", "dart", "go", "java", "kotlin", "php", "python",
                    "ruby", "rust", "typescript", "unreal",
                },
                ids);
        }

        /// <summary>
        /// Every language can name every scalar type.
        /// </summary>
        [Fact]
        public void Every_language_can_name_every_scalar_type()
        {
            var missing = new List<string>();

            foreach (var profile in Profiles())
            {
                foreach (var type in RenderableScalars())
                {
                    if (!profile.ScalarTypes.ContainsKey(type))
                        missing.Add($"  {profile.Id} has no name for {type}");
                }
            }

            Assert.True(missing.Count == 0,
                $"A type was added to ValueType and not to every language:{Environment.NewLine}" +
                string.Join(Environment.NewLine, missing));
        }

        /// <summary>
        /// And every array form resolves to its element, so a generator can name an array by
        /// naming the element and wrapping it.
        /// </summary>
        [Fact]
        public void Every_language_can_name_every_array_type()
        {
            var missing = new List<string>();

            foreach (var profile in Profiles())
            {
                foreach (var element in RenderableScalars())
                {
                    var array = SheetMan.Models.ValueTypes.ArrayOf(element);

                    if (array == ValueType.None)
                    {
                        missing.Add($"  {element} has no array form");
                        continue;
                    }

                    // ScalarTypeName takes an array as readily as a scalar and answers for
                    // its element, which is the contract every generator relies on.
                    string named = profile.ScalarTypeName(array);

                    Assert.False(string.IsNullOrWhiteSpace(named),
                        $"{profile.Id} named {array} as blank.");

                    Assert.Equal(profile.ScalarTypeName(element), named);
                }
            }

            Assert.True(missing.Count == 0,
                $"An array form is missing:{Environment.NewLine}" + string.Join(Environment.NewLine, missing));
        }

        /// <summary>
        /// A type no language should be asked for is refused by name, and the message says
        /// which language refused it.
        ///
        /// `Unresolved` is the one to ask with: a field still holding it means reference
        /// resolution never ran, and a generator that rendered it as something would emit
        /// code around a placeholder.
        /// </summary>
        [Fact]
        public void A_type_a_language_cannot_render_is_refused_by_name()
        {
            foreach (var profile in Profiles())
            {
                var ex = Assert.Throws<SheetManException>(
                    () => profile.ScalarTypeName(ValueType.Unresolved));

                Assert.Contains(profile.Id, ex.Message);
                Assert.Contains("Unresolved", ex.Message);
            }
        }

        /// <summary>
        /// A profile that escapes nothing has to mean it.
        ///
        /// Four of them carry an empty reserved list - C#, Go, PHP and Unreal - and each has
        /// a reason recorded beside it. What makes those reasons trustworthy is not the
        /// comment: the reserved-words fixture compiles every language's output, so a wrong
        /// one is a failing build. This only pins that the escape format is still usable if
        /// the list ever fills, because a format with no placeholder would silently produce
        /// the same name back.
        /// </summary>
        [Fact]
        public void Every_escape_format_actually_changes_the_name()
        {
            foreach (var profile in Profiles())
            {
                Assert.Contains("{0}", profile.MemberNameEscape);

                string escaped = string.Format(profile.MemberNameEscape, "name");

                Assert.NotEqual("name", escaped);
            }
        }

        /// <summary>
        /// Every reserved name a profile lists is actually escaped, and nothing else is.
        /// </summary>
        [Fact]
        public void Only_the_reserved_names_are_escaped()
        {
            foreach (var profile in Profiles())
            {
                foreach (var reserved in profile.ReservedMemberNames)
                    Assert.NotEqual(reserved, profile.MemberName(reserved));

                // A name nothing could reserve, so it must come back untouched.
                Assert.Equal("sheetManOrdinaryName", profile.MemberName("sheetManOrdinaryName"));
            }
        }
    }
}
