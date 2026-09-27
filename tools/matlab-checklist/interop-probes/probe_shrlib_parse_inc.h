/* probe_shrlib_parse_inc.h: included by probe_shrlib_parse.h, for addheader and for which types
 * an included header contributes. */

#ifndef PROBE_SHRLIB_PARSE_INC_H
#define PROBE_SHRLIB_PARSE_INC_H

typedef struct INC_S { int inc_a; float inc_b; } INC_S;

QAPI int q_inc_fn(INC_S s);
QAPI int q_inc_ptr(INC_S *s);

#endif
