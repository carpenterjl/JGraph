/* probe_shrlib_parse.h: declarations whose MATLAB type names, calltypes and recorded structs and
 * enums probe_shrlib_parse asks R2025b for (interop plan, stage 8). Nothing here is exported by
 * jgtestlib.dll, so every function lands in notfound; the prototype file R2025b writes still
 * records each one, which is the answer. */

#ifndef PROBE_SHRLIB_PARSE_H
#define PROBE_SHRLIB_PARSE_H

#include <stddef.h>
#include <stdint.h>
#include <wchar.h>

#define QAPI __declspec(dllimport)

#include "probe_shrlib_parse_inc.h"

typedef struct tagA { int x; double y; } A_T, *PA_T;
struct B { short s; };
typedef struct { int anon; } ANON_T;
struct UNUSED_S { int never; };
typedef enum { E_ONE = 1, E_TWO = E_ONE << 3, E_CHR = 'a', E_NEG = -5, E_HEX = 0x10 | 0x01, E_NEXT } E_T;
enum F { F0, F1 = -2, F2 };
enum UNUSED_E { U0 };
typedef unsigned long DWORD_T;
typedef int (*cb_t)(int);

#pragma pack(push, 2)
typedef struct P2 { char c; double d; } P2_T;
#pragma pack(pop)

typedef struct M {
    char *s;
    double *pd;
    struct B b;
    struct B *pb;
    E_T e;
    unsigned char u[4];
    wchar_t w[3];
    cb_t cb;
    void *v;
    A_T arr2[2];
    long l;
    unsigned long ul;
    long double ld;
    __int64 i64;
    int grid[2][3];
    char **sp;
} M_T;

QAPI void q_tag(A_T a);
QAPI void q_ptr(PA_T p);
QAPI void q_ptrptr(A_T **pp);
QAPI void q_struct(struct B b);
QAPI void q_bptr(struct B *b);
QAPI void q_anon(ANON_T a);
QAPI void q_enum(E_T e);
QAPI void q_enumptr(E_T *e);
QAPI void q_enumf(enum F f);
QAPI enum F q_enumret(void);
QAPI void q_wchar(wchar_t w);
QAPI void q_wcharptr(wchar_t *s);
QAPI const wchar_t *q_wcharret(void);
QAPI void q_ucharptr(unsigned char *p);
QAPI void q_scharptr(signed char *p);
QAPI void q_voidpp(void **p);
QAPI void q_charppp(char ***p);
QAPI void q_ulongptr(unsigned long *p);
QAPI void q_longptr(long *p);
QAPI void q_i64ptr(__int64 *p);
QAPI void q_ldouble(long double x);
QAPI void q_chararr(char s[16]);
QAPI void q_constptr(double *const p);
QAPI void q_dword(DWORD_T d);
QAPI DWORD_T *q_dwordptr(DWORD_T *d);
QAPI int __stdcall q_std(int x);
QAPI int __cdecl q_cdecl(int x);
QAPI M_T *q_m(M_T *m);
QAPI void q_mval(M_T m);
QAPI void q_p2(P2_T *p);
QAPI void q_noparams();
QAPI unsigned q_unsigned(unsigned x);
QAPI short int q_shortint(long int a, unsigned short int b, long unsigned c);
QAPI size_t q_size(ptrdiff_t d, intptr_t i);
QAPI void q_boolptr(_Bool *b);
QAPI char q_charret(void);
QAPI unsigned char *q_ucharret(void);
QAPI char **q_charpp_ret(void);
QAPI void *q_voidret(void);
QAPI void **q_voidppret(void);
QAPI struct B q_bret(void);
QAPI struct B *q_bptrret(void);
QAPI E_T *q_enumptrret(void);
QAPI void q_fnptr(int (*f)(double, int));
QAPI void q_cb(cb_t f);
QAPI void q_unnamed(int, double);
QAPI float *q_floatptr(float *f);
QAPI void q_2d(int m[2][3]);
QAPI void q_constchar(const char *const s);
QAPI int16_t q_int16ptr(int16_t *p, uint64_t *q);
QAPI extern int q_data;
QAPI extern A_T q_data_struct;
QAPI extern char *q_data_str;

static __inline int q_inline(int x) { return x + 1; }

#endif
