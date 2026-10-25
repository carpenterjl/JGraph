/*
 * tmwtypes.h - the base types of the JGraph Runtime's C API.
 *
 * Part of the JGraph Runtime (compiler and SDK plan, stage K10). The type names are the ones
 * MATLAB's C API uses, so C code written against MATLAB Compiler's runtime compiles against the
 * JGraph Runtime unchanged (decision KO3); everything else here is JGraph's own.
 */
#ifndef JGRT_TMWTYPES_H
#define JGRT_TMWTYPES_H

#include <stddef.h>
#include <stdint.h>
#ifndef __cplusplus
#include <stdbool.h>
#endif

/* Fixed-width integers and floating point. */
typedef int8_t int8_T;
typedef uint8_t uint8_T;
typedef int16_t int16_T;
typedef uint16_t uint16_T;
typedef int32_t int32_T;
typedef uint32_t uint32_T;
typedef int64_t int64_T;
typedef uint64_t uint64_T;
typedef float real32_T;
typedef double real64_T;
typedef double real_T;
typedef double time_T;

/* Machine-width and character types. */
typedef unsigned char boolean_T;
typedef char char_T;
typedef unsigned char uchar_T;
typedef char_T byte_T;
typedef int int_T;
typedef unsigned int uint_T;
typedef unsigned long ulong_T;

/*
 * A UTF-16 code unit, the element of a MATLAB char array: char16_t where the language has it
 * (C++11 as __cplusplus reports it, or a standard library that says so), wchar_t in MSVC's C and
 * older C++ modes, where the two are the same size.
 */
#if (defined(__cplusplus) && __cplusplus >= 201103L) || (defined(_HAS_CHAR16_T_LANGUAGE_SUPPORT) && _HAS_CHAR16_T_LANGUAGE_SUPPORT)
typedef char16_t CHAR16_T;
#elif defined(_MSC_VER)
typedef wchar_t CHAR16_T;
#else
typedef uint16_t CHAR16_T;
#endif

/* Array sizes and indices: pointer-width, as in a 64-bit MATLAB. */
typedef size_t mwSize;
typedef size_t mwIndex;
typedef ptrdiff_t mwSignedIndex;

#define MWSIZE_MAX 281474976710655UL
#define MWINDEX_MAX 281474976710655UL
#define MWSIZE_MIN 0UL
#define MWINDEX_MIN 0UL

/* The longest name a variable, a field or a function may have. */
#define TMW_NAME_LENGTH_MAX 64

#endif /* JGRT_TMWTYPES_H */
