/* Conformance harness for the generated C reader.
 *
 * Reads Vectors.scb through the generated accessor and prints each row in the
 * canonical form described in ../README.md. No parsing here: the generated
 * reader does that, and this only prints.
 *
 * The header is named on the command line by the build, so this file works for
 * any scenario without knowing the accessor's name.
 */

#include SHEETMAN_ACCESSOR_HEADER

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* A JSON string. The corpus holds an empty value, a non-ASCII one and control
 * characters, so this escapes rather than assuming anything is printable.
 *
 * UTF-8 goes through as bytes: the reader hands back exactly what the exporter
 * wrote, and re-encoding it here would be this harness disagreeing with the
 * thing it is meant to be checking. */
static void print_quoted(const char* value)
{
    const unsigned char* at = (const unsigned char*)value;

    putchar('"');

    for (; *at != '\0'; ++at) {
        switch (*at) {
            case '"':  fputs("\\\"", stdout); break;
            case '\\': fputs("\\\\", stdout); break;
            case '\n': fputs("\\n", stdout); break;
            case '\r': fputs("\\r", stdout); break;
            case '\t': fputs("\\t", stdout); break;

            default:
                if (*at < 0x20)
                    printf("\\u%04x", (unsigned)*at);
                else
                    putchar((int)*at);

                break;
        }
    }

    putchar('"');
}

/* A double, with enough digits to survive the round trip.
 *
 * %.17g is what it takes for a double to read back identically; the float is
 * widened to one, which is exactly what the corpus comparison expects. */
static void print_number(double value)
{
    printf("%.17g", value);
}

int main(int argc, char** argv)
{
    /* Zeroed, which is what LoadAll requires: it frees the previous load before
     * swapping in the new one, and on a first call there has to be nothing there. */
    ConformanceData_t data = {0};
    char error[512];
    int32_t row;

    if (argc < 2) {
        fprintf(stderr, "usage: conformance-c <binary-directory>\n");
        return 1;
    }

    memset(error, 0, sizeof error);

    if (!ConformanceData_LoadAll(&data, argv[1], error, sizeof error)) {
        fprintf(stderr, "load failed: %s\n", error);
        return 1;
    }

    putchar('[');

    for (row = 0; row < data.vectors.count; ++row) {
        const ConformanceData_VectorsRecord_t* r = &data.vectors.records[row];
        char uuid[37];
        int32_t i;

        if (row > 0)
            putchar(',');

        printf("{\"index\":%d,", (int)r->index);
        printf("\"intVal\":%d,", (int)r->int_val);

        /* A string, because JSON's single numeric type would round anything
         * past 2^53. */
        printf("\"bigVal\":\"%lld\",", (long long)r->big_val);

        fputs("\"floatVal\":", stdout);
        print_number((double)r->float_val);

        fputs(",\"doubleVal\":", stdout);
        print_number(r->double_val);

        fputs(",\"text\":", stdout);
        print_quoted(r->text);

        printf(",\"flag\":%s,", r->flag ? "true" : "false");

        /* Ticks, which is what the generated fields hold. */
        printf("\"when\":\"%lld\",", (long long)r->when);
        printf("\"span\":\"%lld\",", (long long)r->span);

        sm_uuid_to_string(&r->uid, uuid);
        printf("\"uid\":\"%s\",", uuid);

        printf("\"label\":%d,", (int)r->label);

        fputs("\"ints\":[", stdout);
        for (i = 0; i < r->ints_count; ++i) {
            if (i > 0)
                putchar(',');

            printf("%d", (int)r->ints[i]);
        }

        fputs("],\"strs\":[", stdout);
        for (i = 0; i < r->strs_count; ++i) {
            if (i > 0)
                putchar(',');

            print_quoted(r->strs[i]);
        }

        /* The two array forms whose element read is not the scalar one in a loop. An enum
           element is the one place this target reads into a scratch int and casts. */
        fputs("],\"labels\":[", stdout);
        for (i = 0; i < r->labels_count; ++i) {
            if (i > 0)
                putchar(',');

            printf("%d", (int)r->labels[i]);
        }

        fputs("],\"uids\":[", stdout);
        for (i = 0; i < r->uids_count; ++i) {
            if (i > 0)
                putchar(',');

            sm_uuid_to_string(&r->uids[i], uuid);
            printf("\"%s\"", uuid);
        }

        /* The reference indices, which is what the exporter writes for a foreign field. */
        printf("],\"owner\":%d,\"tier\":%d}",
               (int)r->owner_index, (int)r->tier_index);
    }

    putchar(']');

    ConformanceData_Free(&data);

    return 0;
}
