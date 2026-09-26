/*
 * jgtestlib.h: the C shared library both engines load in the interop parity fixtures
 * (plan step 0). One function or group per row of MATLAB's shared-library type table, plus the
 * declarations MATLAB does not support, so the probes can record what it does with them.
 *
 * Build: tools/interop/build-testlib.ps1. Define JGTESTLIB_BUILD when building the library;
 * a user of the library defines nothing (the same convention as MathWorks' shrhelp.h).
 */

#ifndef JGTESTLIB_H
#define JGTESTLIB_H

#include <stddef.h>
#include <stdint.h>
#include <stdbool.h>

#ifdef _WIN32
#ifdef JGTESTLIB_BUILD
#define JG_API __declspec(dllexport)
#else
#define JG_API __declspec(dllimport)
#endif
#else
#define JG_API
#endif

#define JG_VERSION 3
#define JG_NAME_LEN 8

/* ---- Enums ------------------------------------------------------------------------------ */

typedef enum jg_color { JG_RED = 1, JG_GREEN = 2, JG_BLUE = 4 } jg_color;

/* ---- Structs ---------------------------------------------------------------------------- */

typedef struct jg_point {
    double x;
    double y;
} jg_point;

typedef struct jg_mixed {
    int8_t a;
    double b;
    int16_t c;
    int32_t d[3];
    char name[JG_NAME_LEN];
} jg_mixed;

typedef struct jg_nested {
    jg_point p;
    jg_point q;
    int id;
} jg_nested;

#pragma pack(push, 1)
typedef struct jg_packed {
    char a;
    double b;
    short c;
} jg_packed;
#pragma pack(pop)

/* ---- Declarations MATLAB does not support ------------------------------------------------ */

typedef union jg_union {
    int i;
    float f;
} jg_union;

typedef struct jg_bits {
    unsigned int low : 3;
    unsigned int high : 5;
} jg_bits;

typedef double (*jg_unary_fn)(double);

/* ---- Every primitive, in and returned (x + 1; bool is negated) --------------------------- */

JG_API int8_t jg_int8(int8_t x);
JG_API uint8_t jg_uint8(uint8_t x);
JG_API int16_t jg_int16(int16_t x);
JG_API uint16_t jg_uint16(uint16_t x);
JG_API int32_t jg_int32(int32_t x);
JG_API uint32_t jg_uint32(uint32_t x);
JG_API int64_t jg_int64(int64_t x);
JG_API uint64_t jg_uint64(uint64_t x);
JG_API float jg_float(float x);
JG_API double jg_double(double x);
JG_API char jg_char(char x);
JG_API signed char jg_schar(signed char x);
JG_API unsigned char jg_uchar(unsigned char x);
JG_API short jg_short(short x);
JG_API unsigned short jg_ushort(unsigned short x);
JG_API int jg_int(int x);
JG_API unsigned int jg_uint(unsigned int x);
JG_API long jg_long(long x);
JG_API unsigned long jg_ulong(unsigned long x);
JG_API long long jg_longlong(long long x);
JG_API unsigned long long jg_ulonglong(unsigned long long x);
JG_API size_t jg_size(size_t x);
JG_API bool jg_bool(bool x);
JG_API double jg_mixed_args(short a, int b, double c, float d, int64_t e);
JG_API void jg_void(void);

/* Values past 2^53: 2^53 + 1 and UINT64_MAX. */
JG_API int64_t jg_int64_big(void);
JG_API uint64_t jg_uint64_max(void);

/* ---- T* in/out: multiply n elements in place by k ---------------------------------------- */

JG_API void jg_scale_int8(int8_t *p, int n, int8_t k);
JG_API void jg_scale_uint8(uint8_t *p, int n, uint8_t k);
JG_API void jg_scale_int16(int16_t *p, int n, int16_t k);
JG_API void jg_scale_uint16(uint16_t *p, int n, uint16_t k);
JG_API void jg_scale_int32(int32_t *p, int n, int32_t k);
JG_API void jg_scale_uint32(uint32_t *p, int n, uint32_t k);
JG_API void jg_scale_int64(int64_t *p, int n, int64_t k);
JG_API void jg_scale_uint64(uint64_t *p, int n, uint64_t k);
JG_API void jg_scale_float(float *p, int n, float k);
JG_API void jg_scale_double(double *p, int n, double k);
JG_API void jg_negate_bool(bool *p, int n);

JG_API double jg_sum(const double *p, int n);
JG_API double jg_add_ref(double a, double *b, double c); /* *b += a; returns a + *b + c */
JG_API double jg_sum2d(double m[][3], int rows);

/* ---- Memory the library owns ------------------------------------------------------------- */

JG_API int jg_alloc_doubles(double **out, int n); /* *out = 1..n; free with jg_free */
JG_API double *jg_alloc_ret(int n);
JG_API void jg_free(void *p);
JG_API double *jg_static_block(void);   /* six doubles, 1..6, static storage */
JG_API int32_t *jg_static_ints(void);   /* four int32, 10, 20, 30, 40 */
JG_API int jg_is_null(const void *p);

/* A pointer the library keeps and writes through later (the async-buffer case). */
JG_API void jg_keep(double *p, int n);
JG_API int jg_write_kept(double v);     /* writes v to every kept element; returns the count */
JG_API void jg_release_kept(void);

/* ---- Strings ----------------------------------------------------------------------------- */

JG_API const char *jg_greeting(void);
JG_API int jg_strlen(const char *s);
JG_API void jg_upper(char *s);
JG_API char *jg_upper_ret(char *s);
JG_API void jg_fill_name(char *buf, int size);
JG_API const char **jg_words(void);      /* three strings, NULL-terminated, static storage */
JG_API int jg_total_len(char **words, int n);
JG_API void jg_pick_word(int index, const char **out);

/* ---- void* handles ----------------------------------------------------------------------- */

JG_API void *jg_opaque_new(double v);
JG_API double jg_opaque_get(void *h);
JG_API void jg_opaque_free(void *h);

/* ---- Structs by value and by pointer ----------------------------------------------------- */

JG_API double jg_point_len(jg_point p);
JG_API void jg_point_scale(jg_point *p, double k);
JG_API jg_point jg_point_make(double x, double y);
JG_API void jg_point_alloc(jg_point **out);
JG_API double jg_mixed_sum(const jg_mixed *m);
JG_API void jg_mixed_fill(jg_mixed *m);
JG_API double jg_nested_sum(const jg_nested *n);
JG_API double jg_packed_sum(const jg_packed *p);
JG_API void jg_packed_fill(jg_packed *p);

/* Layout witnesses for the struct-layout unit tests: which selects a sizeof or an offsetof,
 * 0 sizeof(jg_point), 1 sizeof(jg_mixed), 2..6 offsetof(jg_mixed, a b c d name),
 * 7 sizeof(jg_nested), 8 offsetof(jg_nested, q), 9 offsetof(jg_nested, id),
 * 10 sizeof(jg_packed), 11..13 offsetof(jg_packed, a b c). Anything else returns (size_t)-1. */
JG_API size_t jg_layout(int which);

/* ---- Enums in and out -------------------------------------------------------------------- */

JG_API const char *jg_color_name(jg_color c);
JG_API jg_color jg_color_next(jg_color c);
JG_API int jg_color_value(jg_color c);

/* ---- Declared, not exported: loadlibrary's notfound list --------------------------------- */

JG_API double jg_not_exported(double x);

/* ---- Unsupported by MATLAB (what does loadlibrary do with each?) ------------------------- */

JG_API int jg_union_in(jg_union u);
JG_API int jg_bits_in(const jg_bits *b);
JG_API double jg_apply(jg_unary_fn f, double x);
JG_API int jg_varsum(int n, ...);
JG_API int jg_triple(double ***p);

/* ---- An exported variable ---------------------------------------------------------------- */

JG_API extern double jg_exported_value;

/* ---- Process behaviour: crash, cancel, stdout, environment, working folder --------------- */

JG_API void jg_crash(void);              /* dereferences NULL */
JG_API void jg_sleep(int ms);
JG_API int jg_printf(const char *s);     /* prints s and a newline to stdout; returns the length */
JG_API const char *jg_getenv(const char *name);  /* "" when unset */
JG_API const char *jg_getcwd(void);
JG_API int jg_version(void);             /* JG_VERSION */

#endif /* JGTESTLIB_H */
