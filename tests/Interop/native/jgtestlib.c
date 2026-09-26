/*
 * jgtestlib.c: the implementation of jgtestlib.h and jgtestlib_win.h. See those headers.
 * Build: tools/interop/build-testlib.ps1 (cl /LD /O2 /DJGTESTLIB_BUILD).
 */

#define _CRT_SECURE_NO_WARNINGS
#ifndef JGTESTLIB_BUILD
#define JGTESTLIB_BUILD
#endif
#include "jgtestlib_win.h"

#include <ctype.h>
#include <direct.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

/* ---- Primitives -------------------------------------------------------------------------- */

JG_API int8_t jg_int8(int8_t x) { return (int8_t)(x + 1); }
JG_API uint8_t jg_uint8(uint8_t x) { return (uint8_t)(x + 1); }
JG_API int16_t jg_int16(int16_t x) { return (int16_t)(x + 1); }
JG_API uint16_t jg_uint16(uint16_t x) { return (uint16_t)(x + 1); }
JG_API int32_t jg_int32(int32_t x) { return x + 1; }
JG_API uint32_t jg_uint32(uint32_t x) { return x + 1; }
JG_API int64_t jg_int64(int64_t x) { return x + 1; }
JG_API uint64_t jg_uint64(uint64_t x) { return x + 1; }
JG_API float jg_float(float x) { return x + 1; }
JG_API double jg_double(double x) { return x + 1; }
JG_API char jg_char(char x) { return (char)(x + 1); }
JG_API signed char jg_schar(signed char x) { return (signed char)(x + 1); }
JG_API unsigned char jg_uchar(unsigned char x) { return (unsigned char)(x + 1); }
JG_API short jg_short(short x) { return (short)(x + 1); }
JG_API unsigned short jg_ushort(unsigned short x) { return (unsigned short)(x + 1); }
JG_API int jg_int(int x) { return x + 1; }
JG_API unsigned int jg_uint(unsigned int x) { return x + 1; }
JG_API long jg_long(long x) { return x + 1; }
JG_API unsigned long jg_ulong(unsigned long x) { return x + 1; }
JG_API long long jg_longlong(long long x) { return x + 1; }
JG_API unsigned long long jg_ulonglong(unsigned long long x) { return x + 1; }
JG_API size_t jg_size(size_t x) { return x + 1; }
JG_API bool jg_bool(bool x) { return !x; }
JG_API double jg_mixed_args(short a, int b, double c, float d, int64_t e) { return a + b + c + d + (double)e; }
JG_API void jg_void(void) { }

JG_API int64_t jg_int64_big(void) { return ((int64_t)1 << 53) + 1; }
JG_API uint64_t jg_uint64_max(void) { return UINT64_MAX; }

/* ---- T* in/out --------------------------------------------------------------------------- */

#define JG_SCALE(NAME, T) \
    JG_API void NAME(T *p, int n, T k) { for (int i = 0; i < n; i++) p[i] = (T)(p[i] * k); }

JG_SCALE(jg_scale_int8, int8_t)
JG_SCALE(jg_scale_uint8, uint8_t)
JG_SCALE(jg_scale_int16, int16_t)
JG_SCALE(jg_scale_uint16, uint16_t)
JG_SCALE(jg_scale_int32, int32_t)
JG_SCALE(jg_scale_uint32, uint32_t)
JG_SCALE(jg_scale_int64, int64_t)
JG_SCALE(jg_scale_uint64, uint64_t)
JG_SCALE(jg_scale_float, float)
JG_SCALE(jg_scale_double, double)

JG_API void jg_negate_bool(bool *p, int n) { for (int i = 0; i < n; i++) p[i] = !p[i]; }

JG_API double jg_sum(const double *p, int n)
{
    double s = 0;
    for (int i = 0; i < n; i++) s += p[i];
    return s;
}

JG_API double jg_add_ref(double a, double *b, double c)
{
    if (!b) return NAN;
    *b += a;
    return a + *b + c;
}

JG_API double jg_sum2d(double m[][3], int rows)
{
    double s = 0;
    for (int r = 0; r < rows; r++)
        for (int c = 0; c < 3; c++)
            s += m[r][c] * (r * 10 + c + 1);
    return s;
}

/* ---- Memory the library owns ------------------------------------------------------------- */

JG_API int jg_alloc_doubles(double **out, int n)
{
    if (!out) return -2;
    double *p = (double *)malloc(sizeof(double) * (n > 0 ? n : 1));
    if (!p) return -1;
    for (int i = 0; i < n; i++) p[i] = i + 1;
    *out = p;
    return n;
}

JG_API double *jg_alloc_ret(int n)
{
    double *p = NULL;
    return jg_alloc_doubles(&p, n) < 0 ? NULL : p;
}

JG_API void jg_free(void *p) { free(p); }

static double g_block[6] = { 1, 2, 3, 4, 5, 6 };
static int32_t g_ints[4] = { 10, 20, 30, 40 };

JG_API double *jg_static_block(void) { return g_block; }
JG_API int32_t *jg_static_ints(void) { return g_ints; }
JG_API int jg_is_null(const void *p) { return p == NULL; }

static double *g_kept = NULL;
static int g_kept_n = 0;

JG_API void jg_keep(double *p, int n) { g_kept = p; g_kept_n = n; }

JG_API int jg_write_kept(double v)
{
    if (!g_kept) return 0;
    for (int i = 0; i < g_kept_n; i++) g_kept[i] = v;
    return g_kept_n;
}

JG_API void jg_release_kept(void) { g_kept = NULL; g_kept_n = 0; }

/* ---- Strings ----------------------------------------------------------------------------- */

static const char *g_words[] = { "alpha", "beta", "gamma", NULL };

JG_API const char *jg_greeting(void) { return "hello from jgtestlib"; }
JG_API int jg_strlen(const char *s) { return s ? (int)strlen(s) : -1; }

JG_API void jg_upper(char *s)
{
    if (!s) return;
    for (; *s; s++) *s = (char)toupper((unsigned char)*s);
}

JG_API char *jg_upper_ret(char *s)
{
    jg_upper(s);
    return s;
}

JG_API void jg_fill_name(char *buf, int size)
{
    if (!buf || size <= 0) return;
    strncpy(buf, "jgtestlib", (size_t)size - 1);
    buf[size - 1] = '\0';
}

JG_API const char **jg_words(void) { return g_words; }

JG_API int jg_total_len(char **words, int n)
{
    int total = 0;
    for (int i = 0; i < n; i++) total += words[i] ? (int)strlen(words[i]) : 0;
    return total;
}

JG_API void jg_pick_word(int index, const char **out)
{
    if (!out) return;
    *out = (index >= 0 && index < 3) ? g_words[index] : NULL;
}

/* ---- void* handles ----------------------------------------------------------------------- */

JG_API void *jg_opaque_new(double v)
{
    double *p = (double *)malloc(sizeof(double));
    if (p) *p = v;
    return p;
}

JG_API double jg_opaque_get(void *h) { return h ? *(double *)h : NAN; }
JG_API void jg_opaque_free(void *h) { free(h); }

/* Every pointer parameter is NULL-checked: R2025b passes NULL for some libstructs (step 0). */

/* ---- Structs ----------------------------------------------------------------------------- */

JG_API double jg_point_len(jg_point p) { return sqrt(p.x * p.x + p.y * p.y); }

JG_API void jg_point_scale(jg_point *p, double k)
{
    if (!p) return;
    p->x *= k;
    p->y *= k;
}

JG_API jg_point jg_point_make(double x, double y)
{
    jg_point p = { x, y };
    return p;
}

JG_API void jg_point_alloc(jg_point **out)
{
    if (!out) return;
    jg_point *p = (jg_point *)malloc(sizeof(jg_point));
    if (p) { p->x = 3; p->y = 4; }
    *out = p;
}

JG_API double jg_mixed_sum(const jg_mixed *m)
{
    if (!m) return NAN;
    return m->a + m->b + m->c + m->d[0] + m->d[1] + m->d[2] + (double)strlen(m->name);
}

JG_API void jg_mixed_fill(jg_mixed *m)
{
    if (!m) return;
    m->a = -1;
    m->b = 2.5;
    m->c = 300;
    m->d[0] = 4;
    m->d[1] = 5;
    m->d[2] = 6;
    strcpy(m->name, "filled");
}

JG_API double jg_nested_sum(const jg_nested *n) { return !n ? NAN : n->p.x + n->p.y + n->q.x + n->q.y + n->id; }
JG_API double jg_packed_sum(const jg_packed *p) { return !p ? NAN : p->a + p->b + p->c; }

JG_API void jg_packed_fill(jg_packed *p)
{
    if (!p) return;
    p->a = 7;
    p->b = 8.5;
    p->c = 9;
}

JG_API size_t jg_layout(int which)
{
    switch (which) {
    case 0: return sizeof(jg_point);
    case 1: return sizeof(jg_mixed);
    case 2: return offsetof(jg_mixed, a);
    case 3: return offsetof(jg_mixed, b);
    case 4: return offsetof(jg_mixed, c);
    case 5: return offsetof(jg_mixed, d);
    case 6: return offsetof(jg_mixed, name);
    case 7: return sizeof(jg_nested);
    case 8: return offsetof(jg_nested, q);
    case 9: return offsetof(jg_nested, id);
    case 10: return sizeof(jg_packed);
    case 11: return offsetof(jg_packed, a);
    case 12: return offsetof(jg_packed, b);
    case 13: return offsetof(jg_packed, c);
    default: return (size_t)-1;
    }
}

/* ---- Enums ------------------------------------------------------------------------------- */

JG_API const char *jg_color_name(jg_color c)
{
    switch (c) {
    case JG_RED: return "red";
    case JG_GREEN: return "green";
    case JG_BLUE: return "blue";
    default: return "unknown";
    }
}

JG_API jg_color jg_color_next(jg_color c)
{
    switch (c) {
    case JG_RED: return JG_GREEN;
    case JG_GREEN: return JG_BLUE;
    default: return JG_RED;
    }
}

JG_API int jg_color_value(jg_color c) { return (int)c; }

/* jg_not_exported is declared in the header and deliberately never defined. */

/* ---- Unsupported by MATLAB --------------------------------------------------------------- */

JG_API int jg_union_in(jg_union u) { return u.i; }
JG_API int jg_bits_in(const jg_bits *b) { return !b ? -1 : (int)(b->low + 10 * b->high); }
JG_API double jg_apply(jg_unary_fn f, double x) { return f ? f(x) : NAN; }

JG_API int jg_varsum(int n, ...)
{
    va_list ap;
    int s = 0;
    va_start(ap, n);
    for (int i = 0; i < n; i++) s += va_arg(ap, int);
    va_end(ap);
    return s;
}

JG_API int jg_triple(double ***p) { return p != NULL; }

/* ---- An exported variable ---------------------------------------------------------------- */

JG_API double jg_exported_value = 12.5;

/* ---- Process behaviour ------------------------------------------------------------------- */

JG_API void jg_crash(void)
{
    volatile int *p = NULL;
    *p = 1;
}

JG_API void jg_sleep(int ms) { Sleep((DWORD)(ms < 0 ? 0 : ms)); }

JG_API int jg_printf(const char *s)
{
    int n = printf("%s\n", s ? s : "(null)");
    fflush(stdout);
    return n;
}

/* The Win32 process environment, not the CRT's: with /MT the DLL's CRT keeps its own copy of the
 * environment from load time, which is not what a caller's setenv changes. */
static char g_env[32768];

JG_API const char *jg_getenv(const char *name)
{
    DWORD n = name ? GetEnvironmentVariableA(name, g_env, (DWORD)sizeof g_env) : 0;
    if (n == 0 || n >= sizeof g_env) g_env[0] = '\0';
    return g_env;
}

static char g_cwd[4096];

JG_API const char *jg_getcwd(void)
{
    if (!_getcwd(g_cwd, (int)sizeof g_cwd)) g_cwd[0] = '\0';
    return g_cwd;
}

JG_API int jg_version(void) { return JG_VERSION; }

/* ---- jgtestlib_win.h --------------------------------------------------------------------- */

JG_API DWORD jg_tick(void) { return GetTickCount(); }
JG_API BOOL jg_is_even(DWORD x) { return (x % 2) == 0; }
