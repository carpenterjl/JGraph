// AMD, Copyright (c) 1996-2022, Timothy A. Davis, Patrick R. Amestoy, and Iain S. Duff.
// All Rights Reserved.
// SPDX-License-Identifier: BSD-3-Clause
//
// This file is a C# transcription of SuiteSparse AMD's amd_order, amd_aat, amd_1, amd_2,
// amd_postorder and amd_post_tree (AMD/Source in SuiteSparse), made for JGraph (open item 345,
// ADR 0381). It is distributed under AMD's own licence, reproduced here:
//
// Redistribution and use in source and binary forms, with or without modification, are permitted
// provided that the following conditions are met:
//   * Redistributions of source code must retain the above copyright notice, this list of
//     conditions and the following disclaimer.
//   * Redistributions in binary form must reproduce the above copyright notice, this list of
//     conditions and the following disclaimer in the documentation and/or other materials provided
//     with the distribution.
//   * Neither the name of the organizations to which the authors are affiliated, nor the names of
//     its contributors may be used to endorse or promote products derived from this software
//     without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
// IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR
// CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER
// IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT
// OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

namespace JGraph.Scripting.Jgs;

/// <summary>
/// SuiteSparse AMD, the approximate minimum degree ordering R2025b's <c>amd</c> is (open item 345,
/// ADR 0381). A transcription, statement for statement, of the C source: the ordering's ties are
/// broken by the order in which the degree lists, the hash buckets and the element lists are walked,
/// and no rule read off a few answers reproduces them. The integers are 64-bit, as in the
/// <c>SuiteSparse_long</c> build MATLAB calls.
/// </summary>
internal static class SuiteSparseAmd
{
    private const long Empty = -1;

    /// <summary>AMD_INFO's length and the slots <c>[p, Info] = amd(A)</c> fills.</summary>
    internal const int InfoLength = 20;

    private const int InfoStatus = 0, InfoN = 1, InfoNz = 2, InfoSymmetry = 3, InfoNzDiag = 4, InfoNzAPlusAt = 5,
        InfoNDense = 6, InfoMemory = 7, InfoNcmpa = 8, InfoLnz = 9, InfoNdiv = 10, InfoNmsLdl = 11, InfoNmsLu = 12,
        InfoDmax = 13;

    /// <summary>AMD's default control: dense rows past 10·√n, aggressive absorption on.</summary>
    internal const double DefaultDense = 10;

    private static long Flip(long i) => -i - 2;

    /// <summary>
    /// amd_order on a column-compressed pattern with sorted, unduplicated row indices (MATLAB's own
    /// storage, so the jumbled path never runs). Answers the permutation, zero-based, and fills
    /// <paramref name="info"/> (length <see cref="InfoLength"/>).
    /// </summary>
    internal static int[] Order(int n, int[] ap, int[] ai, double dense, bool aggressive, double[] info)
    {
        for (int i = 0; i < InfoLength; i++)
        {
            info[i] = Empty;
        }

        info[InfoN] = n;
        info[InfoStatus] = 0;
        if (n == 0)
        {
            return [];
        }

        long nz = ap[n];
        info[InfoNz] = nz;
        double mem = 0;

        var len = new long[n];
        var pinv = new long[n];
        mem += n;
        mem += n;

        var p = new long[n];
        long nzaat = Aat(n, ap, ai, len, p, info);

        long slen = nzaat;
        slen += nzaat / 5;
        for (int i = 0; i < 7; i++)
        {
            slen += n;
        }

        mem += slen;
        var s = new long[slen];
        info[InfoMemory] = mem * sizeof(long);

        Amd1(n, ap, ai, p, pinv, len, slen, s, dense, aggressive, info);
        info[InfoStatus] = 0;

        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = (int)p[i];
        }

        return order;
    }

    /// <summary>amd_aat: the column counts of A + Aᵀ without the diagonal, and the pattern's symmetry.</summary>
    private static long Aat(int n, int[] ap, int[] ai, long[] len, long[] tp, double[] info)
    {
        for (int k = 0; k < n; k++)
        {
            len[k] = 0;
        }

        long nzdiag = 0;
        long nzboth = 0;
        long nz = ap[n];

        for (int k = 0; k < n; k++)
        {
            long p1 = ap[k];
            long p2 = ap[k + 1];

            long p;
            for (p = p1; p < p2;)
            {
                long j = ai[p];
                if (j < k)
                {
                    len[j]++;
                    len[k]++;
                    p++;
                }
                else if (j == k)
                {
                    p++;
                    nzdiag++;
                    break;
                }
                else
                {
                    break;
                }

                long pj2 = ap[j + 1];
                long pj;
                for (pj = tp[j]; pj < pj2;)
                {
                    long i = ai[pj];
                    if (i < k)
                    {
                        len[i]++;
                        len[j]++;
                        pj++;
                    }
                    else if (i == k)
                    {
                        pj++;
                        nzboth++;
                        break;
                    }
                    else
                    {
                        break;
                    }
                }

                tp[j] = pj;
            }

            tp[k] = p;
        }

        for (int j = 0; j < n; j++)
        {
            for (long pj = tp[j]; pj < ap[j + 1]; pj++)
            {
                long i = ai[pj];
                len[i]++;
                len[j]++;
            }
        }

        double sym = nz == nzdiag ? 1 : (2 * (double)nzboth) / (double)(nz - nzdiag);

        long nzaat = 0;
        for (int k = 0; k < n; k++)
        {
            nzaat += len[k];
        }

        info[InfoStatus] = 0;
        info[InfoN] = n;
        info[InfoNz] = nz;
        info[InfoSymmetry] = sym;
        info[InfoNzDiag] = nzdiag;
        info[InfoNzAPlusAt] = nzaat;
        return nzaat;
    }

    /// <summary>amd_1: lays A + Aᵀ out in the workspace, row by row with elbow room, and orders it.</summary>
    private static void Amd1(int n, int[] ap, int[] ai, long[] pOut, long[] pinv, long[] len, long slen, long[] s,
        double dense, bool aggressive, double[] info)
    {
        long iwlen = slen - 6L * n;
        var pe = new long[n];
        var nv = new long[n];
        var head = new long[n];
        var elen = new long[n];
        var degree = new long[n];
        var w = new long[n];
        var iw = new long[iwlen];

        long[] sp = nv;
        long[] tp = w;
        long pfree = 0;
        for (int j = 0; j < n; j++)
        {
            pe[j] = pfree;
            sp[j] = pfree;
            pfree += len[j];
        }

        for (int k = 0; k < n; k++)
        {
            long p1 = ap[k];
            long p2 = ap[k + 1];

            long p;
            for (p = p1; p < p2;)
            {
                long j = ai[p];
                if (j < k)
                {
                    iw[sp[j]++] = k;
                    iw[sp[k]++] = j;
                    p++;
                }
                else if (j == k)
                {
                    p++;
                    break;
                }
                else
                {
                    break;
                }

                long pj2 = ap[j + 1];
                long pj;
                for (pj = tp[j]; pj < pj2;)
                {
                    long i = ai[pj];
                    if (i < k)
                    {
                        iw[sp[i]++] = j;
                        iw[sp[j]++] = i;
                        pj++;
                    }
                    else if (i == k)
                    {
                        pj++;
                        break;
                    }
                    else
                    {
                        break;
                    }
                }

                tp[j] = pj;
            }

            tp[k] = p;
        }

        for (int j = 0; j < n; j++)
        {
            for (long pj = tp[j]; pj < ap[j + 1]; pj++)
            {
                long i = ai[pj];
                iw[sp[i]++] = j;
                iw[sp[j]++] = i;
            }
        }

        Amd2(n, pe, iw, len, iwlen, pfree, nv, pinv, pOut, head, elen, degree, w, dense, aggressive, info);
    }

    private static long ClearFlag(long wflg, long wbig, long[] w, long n)
    {
        if (wflg < 2 || wflg >= wbig)
        {
            for (long x = 0; x < n; x++)
            {
                if (w[x] != 0)
                {
                    w[x] = 1;
                }
            }

            wflg = 2;
        }

        return wflg;
    }

    /// <summary>The ordering's dense threshold, as amd_2 computes it from Control[AMD_DENSE].</summary>
    private static long DenseThreshold(double alpha, long n)
    {
        double d = alpha < 0 ? n - 2 : alpha * Math.Sqrt(n);
        long dense = double.IsNaN(d) ? long.MinValue
            : d >= long.MaxValue ? long.MaxValue
            : d <= long.MinValue ? long.MinValue
            : (long)d;
        dense = Math.Max(16, dense);
        return Math.Min(n, dense);
    }

    /// <summary>amd_2: the approximate minimum degree ordering itself, then the assembly tree's postorder.</summary>
    private static void Amd2(long n, long[] pe, long[] iw, long[] len, long iwlen, long pfree,
        long[] nv, long[] next, long[] last, long[] head, long[] elen, long[] degree, long[] w,
        double alpha, bool aggressive, double[] info)
    {
        long deg, degme, dext, lemax, e, elenme, eln, i, ilast, inext, j,
            jlast, jnext, k, knt1, knt2, knt3, lenj, ln, me, mindeg, nel, nleft,
            nvi, nvj, nvpiv, slenme, wbig, we, wflg, wnvi, ndense, ncmpa, dense;
        bool ok;
        ulong hash;
        double f, r, ndiv, s, nmsLu, nmsLdl, dmax, lnz, lnzme;
        long p, p1, p2, p3, p4, pdst, pend, pj, pme, pme1, pme2, pn, psrc;

        lnz = 0;
        ndiv = 0;
        nmsLu = 0;
        nmsLdl = 0;
        dmax = 1;
        me = Empty;

        mindeg = 0;
        ncmpa = 0;
        nel = 0;
        lemax = 0;

        dense = DenseThreshold(alpha, n);

        for (i = 0; i < n; i++)
        {
            last[i] = Empty;
            head[i] = Empty;
            next[i] = Empty;
            nv[i] = 1;
            w[i] = 1;
            elen[i] = 0;
            degree[i] = len[i];
        }

        wbig = long.MaxValue - n;
        wflg = ClearFlag(0, wbig, w, n);

        // --- initialize degree lists and eliminate dense and empty rows ---
        ndense = 0;

        for (i = 0; i < n; i++)
        {
            deg = degree[i];
            if (deg == 0)
            {
                elen[i] = Flip(1);
                nel++;
                pe[i] = Empty;
                w[i] = 0;
            }
            else if (deg > dense)
            {
                ndense++;
                nv[i] = 0;
                elen[i] = Empty;
                nel++;
                pe[i] = Empty;
            }
            else
            {
                inext = head[deg];
                if (inext != Empty)
                {
                    last[inext] = i;
                }

                next[i] = inext;
                head[deg] = i;
            }
        }

        // --- while (selecting pivots) ---
        while (nel < n)
        {
            // get pivot of minimum degree
            for (deg = mindeg; deg < n; deg++)
            {
                me = head[deg];
                if (me != Empty)
                {
                    break;
                }
            }

            mindeg = deg;

            // remove chosen variable from link list
            inext = next[me];
            if (inext != Empty)
            {
                last[inext] = Empty;
            }

            head[deg] = inext;

            elenme = elen[me];
            nvpiv = nv[me];
            nel += nvpiv;

            // --- construct new element ---
            nv[me] = -nvpiv;
            degme = 0;

            if (elenme == 0)
            {
                // construct the new element in place
                pme1 = pe[me];
                pme2 = pme1 - 1;

                for (p = pme1; p <= pme1 + len[me] - 1; p++)
                {
                    i = iw[p];
                    nvi = nv[i];
                    if (nvi > 0)
                    {
                        degme += nvi;
                        nv[i] = -nvi;
                        iw[++pme2] = i;

                        ilast = last[i];
                        inext = next[i];
                        if (inext != Empty)
                        {
                            last[inext] = ilast;
                        }

                        if (ilast != Empty)
                        {
                            next[ilast] = inext;
                        }
                        else
                        {
                            head[degree[i]] = inext;
                        }
                    }
                }
            }
            else
            {
                // construct the new element in empty space, Iw [pfree ...]
                p = pe[me];
                pme1 = pfree;
                slenme = len[me] - elenme;

                for (knt1 = 1; knt1 <= elenme + 1; knt1++)
                {
                    if (knt1 > elenme)
                    {
                        e = me;
                        pj = p;
                        ln = slenme;
                    }
                    else
                    {
                        e = iw[p++];
                        pj = pe[e];
                        ln = len[e];
                    }

                    for (knt2 = 1; knt2 <= ln; knt2++)
                    {
                        i = iw[pj++];
                        nvi = nv[i];

                        if (nvi > 0)
                        {
                            // compress Iw, if necessary
                            if (pfree >= iwlen)
                            {
                                pe[me] = p;
                                len[me] -= knt1;
                                if (len[me] == 0)
                                {
                                    pe[me] = Empty;
                                }

                                pe[e] = pj;
                                len[e] = ln - knt2;
                                if (len[e] == 0)
                                {
                                    pe[e] = Empty;
                                }

                                ncmpa++;

                                for (j = 0; j < n; j++)
                                {
                                    pn = pe[j];
                                    if (pn >= 0)
                                    {
                                        pe[j] = iw[pn];
                                        iw[pn] = Flip(j);
                                    }
                                }

                                psrc = 0;
                                pdst = 0;
                                pend = pme1 - 1;

                                while (psrc <= pend)
                                {
                                    j = Flip(iw[psrc++]);
                                    if (j >= 0)
                                    {
                                        iw[pdst] = pe[j];
                                        pe[j] = pdst++;
                                        lenj = len[j];
                                        for (knt3 = 0; knt3 <= lenj - 2; knt3++)
                                        {
                                            iw[pdst++] = iw[psrc++];
                                        }
                                    }
                                }

                                p1 = pdst;
                                for (psrc = pme1; psrc <= pfree - 1; psrc++)
                                {
                                    iw[pdst++] = iw[psrc];
                                }

                                pme1 = p1;
                                pfree = pdst;
                                pj = pe[e];
                                p = pe[me];
                            }

                            // i is a principal variable not yet placed in Lme
                            degme += nvi;
                            nv[i] = -nvi;
                            iw[pfree++] = i;

                            ilast = last[i];
                            inext = next[i];
                            if (inext != Empty)
                            {
                                last[inext] = ilast;
                            }

                            if (ilast != Empty)
                            {
                                next[ilast] = inext;
                            }
                            else
                            {
                                head[degree[i]] = inext;
                            }
                        }
                    }

                    if (e != me)
                    {
                        pe[e] = Flip(me);
                        w[e] = 0;
                    }
                }

                pme2 = pfree - 1;
            }

            // me has now been converted into an element in Iw [pme1..pme2]
            degree[me] = degme;
            pe[me] = pme1;
            len[me] = pme2 - pme1 + 1;

            elen[me] = Flip(nvpiv + degme);

            wflg = ClearFlag(wflg, wbig, w, n);

            // --- compute (W [e] - wflg) = |Le\Lme| for all elements ---
            for (pme = pme1; pme <= pme2; pme++)
            {
                i = iw[pme];
                eln = elen[i];
                if (eln > 0)
                {
                    nvi = -nv[i];
                    wnvi = wflg - nvi;
                    for (p = pe[i]; p <= pe[i] + eln - 1; p++)
                    {
                        e = iw[p];
                        we = w[e];
                        if (we >= wflg)
                        {
                            we -= nvi;
                        }
                        else if (we != 0)
                        {
                            we = degree[e] + wnvi;
                        }

                        w[e] = we;
                    }
                }
            }

            // --- degree update and element absorption ---
            for (pme = pme1; pme <= pme2; pme++)
            {
                i = iw[pme];
                p1 = pe[i];
                p2 = p1 + elen[i] - 1;
                pn = p1;
                hash = 0;
                deg = 0;

                if (aggressive)
                {
                    for (p = p1; p <= p2; p++)
                    {
                        e = iw[p];
                        we = w[e];
                        if (we != 0)
                        {
                            dext = we - wflg;
                            if (dext > 0)
                            {
                                deg += dext;
                                iw[pn++] = e;
                                hash += (ulong)e;
                            }
                            else
                            {
                                pe[e] = Flip(me);
                                w[e] = 0;
                            }
                        }
                    }
                }
                else
                {
                    for (p = p1; p <= p2; p++)
                    {
                        e = iw[p];
                        we = w[e];
                        if (we != 0)
                        {
                            dext = we - wflg;
                            deg += dext;
                            iw[pn++] = e;
                            hash += (ulong)e;
                        }
                    }
                }

                elen[i] = pn - p1 + 1;

                p3 = pn;
                p4 = p1 + len[i];
                for (p = p2 + 1; p < p4; p++)
                {
                    j = iw[p];
                    nvj = nv[j];
                    if (nvj > 0)
                    {
                        deg += nvj;
                        iw[pn++] = j;
                        hash += (ulong)j;
                    }
                }

                if (elen[i] == 1 && p3 == pn)
                {
                    // mass elimination
                    pe[i] = Flip(me);
                    nvi = -nv[i];
                    degme -= nvi;
                    nvpiv += nvi;
                    nel += nvi;
                    nv[i] = 0;
                    elen[i] = Empty;
                }
                else
                {
                    degree[i] = Math.Min(degree[i], deg);

                    iw[pn] = iw[p3];
                    iw[p3] = iw[p1];
                    iw[p1] = me;
                    len[i] = pn - p1 + 1;

                    hash %= (ulong)n;
                    long bucket = (long)hash;

                    j = head[bucket];
                    if (j <= Empty)
                    {
                        next[i] = Flip(j);
                        head[bucket] = Flip(i);
                    }
                    else
                    {
                        next[i] = last[j];
                        last[j] = i;
                    }

                    last[i] = bucket;
                }
            }

            degree[me] = degme;

            lemax = Math.Max(lemax, degme);
            wflg += lemax;
            wflg = ClearFlag(wflg, wbig, w, n);

            // --- supervariable detection ---
            for (pme = pme1; pme <= pme2; pme++)
            {
                i = iw[pme];
                if (nv[i] < 0)
                {
                    long bucket = last[i];

                    j = head[bucket];
                    if (j == Empty)
                    {
                        i = Empty;
                    }
                    else if (j < Empty)
                    {
                        i = Flip(j);
                        head[bucket] = Empty;
                    }
                    else
                    {
                        i = last[j];
                        last[j] = Empty;
                    }

                    while (i != Empty && next[i] != Empty)
                    {
                        ln = len[i];
                        eln = elen[i];
                        for (p = pe[i] + 1; p <= pe[i] + ln - 1; p++)
                        {
                            w[iw[p]] = wflg;
                        }

                        jlast = i;
                        j = next[i];

                        while (j != Empty)
                        {
                            ok = len[j] == ln && elen[j] == eln;
                            for (p = pe[j] + 1; ok && p <= pe[j] + ln - 1; p++)
                            {
                                if (w[iw[p]] != wflg)
                                {
                                    ok = false;
                                }
                            }

                            if (ok)
                            {
                                pe[j] = Flip(i);
                                nv[i] += nv[j];
                                nv[j] = 0;
                                elen[j] = Empty;
                                j = next[j];
                                next[jlast] = j;
                            }
                            else
                            {
                                jlast = j;
                                j = next[j];
                            }
                        }

                        wflg++;
                        i = next[i];
                    }
                }
            }

            // --- restore degree lists and remove nonprincipal supervariables from element ---
            p = pme1;
            nleft = n - nel;
            for (pme = pme1; pme <= pme2; pme++)
            {
                i = iw[pme];
                nvi = -nv[i];
                if (nvi > 0)
                {
                    nv[i] = nvi;

                    deg = degree[i] + degme - nvi;
                    deg = Math.Min(deg, nleft - nvi);

                    inext = head[deg];
                    if (inext != Empty)
                    {
                        last[inext] = i;
                    }

                    next[i] = inext;
                    last[i] = Empty;
                    head[deg] = i;

                    mindeg = Math.Min(mindeg, deg);
                    degree[i] = deg;

                    iw[p++] = i;
                }
            }

            // --- finalize the new element ---
            nv[me] = nvpiv;
            len[me] = p - pme1;
            if (len[me] == 0)
            {
                pe[me] = Empty;
                w[me] = 0;
            }

            if (elenme != 0)
            {
                pfree = p;
            }

            f = nvpiv;
            r = degme + ndense;
            dmax = Math.Max(dmax, f + r);

            lnzme = f * r + (f - 1) * f / 2;
            lnz += lnzme;

            ndiv += lnzme;

            s = f * r * r + r * (f - 1) * f + (f - 1) * f * (2 * f - 1) / 6;
            nmsLu += s;

            nmsLdl += (s + lnzme) / 2;
        }

        // --- done selecting pivots ---
        f = ndense;
        dmax = Math.Max(dmax, (double)ndense);

        lnzme = (f - 1) * f / 2;
        lnz += lnzme;

        ndiv += lnzme;

        s = (f - 1) * f * (2 * f - 1) / 6;
        nmsLu += s;

        nmsLdl += (s + lnzme) / 2;

        info[InfoLnz] = lnz;
        info[InfoNdiv] = ndiv;
        info[InfoNmsLdl] = nmsLdl;
        info[InfoNmsLu] = nmsLu;
        info[InfoNDense] = ndense;
        info[InfoDmax] = dmax;
        info[InfoNcmpa] = ncmpa;
        info[InfoStatus] = 0;

        // --- post-ordering ---
        for (i = 0; i < n; i++)
        {
            pe[i] = Flip(pe[i]);
        }

        for (i = 0; i < n; i++)
        {
            elen[i] = Flip(elen[i]);
        }

        // compress the paths of the variables
        for (i = 0; i < n; i++)
        {
            if (nv[i] == 0)
            {
                j = pe[i];
                if (j == Empty)
                {
                    continue;
                }

                while (nv[j] == 0)
                {
                    j = pe[j];
                }

                e = j;

                j = i;
                while (nv[j] == 0)
                {
                    jnext = pe[j];
                    pe[j] = e;
                    j = jnext;
                }
            }
        }

        // postorder the assembly tree
        PostOrder(n, pe, nv, elen, w, head, next, last);

        // compute output permutation and inverse permutation
        for (k = 0; k < n; k++)
        {
            head[k] = Empty;
            next[k] = Empty;
        }

        for (e = 0; e < n; e++)
        {
            k = w[e];
            if (k != Empty)
            {
                head[k] = e;
            }
        }

        nel = 0;
        for (k = 0; k < n; k++)
        {
            e = head[k];
            if (e == Empty)
            {
                break;
            }

            next[e] = nel;
            nel += nv[e];
        }

        for (i = 0; i < n; i++)
        {
            if (nv[i] == 0)
            {
                e = pe[i];
                if (e != Empty)
                {
                    next[i] = next[e];
                    next[e]++;
                }
                else
                {
                    next[i] = nel++;
                }
            }
        }

        for (i = 0; i < n; i++)
        {
            k = next[i];
            last[k] = i;
        }
    }

    /// <summary>amd_postorder: the children of each node with the largest last, then a depth-first postorder.</summary>
    private static void PostOrder(long nn, long[] parent, long[] nv, long[] fsize, long[] order,
        long[] child, long[] sibling, long[] stack)
    {
        long i, j, k, par, frsize, f, fprev, maxfrsize, bigfprev, bigf, fnext;

        for (j = 0; j < nn; j++)
        {
            child[j] = Empty;
            sibling[j] = Empty;
        }

        for (j = nn - 1; j >= 0; j--)
        {
            if (nv[j] > 0)
            {
                par = parent[j];
                if (par != Empty)
                {
                    sibling[j] = child[par];
                    child[par] = j;
                }
            }
        }

        for (i = 0; i < nn; i++)
        {
            if (nv[i] > 0 && child[i] != Empty)
            {
                fprev = Empty;
                maxfrsize = Empty;
                bigfprev = Empty;
                bigf = Empty;
                for (f = child[i]; f != Empty; f = sibling[f])
                {
                    frsize = fsize[f];
                    if (frsize >= maxfrsize)
                    {
                        maxfrsize = frsize;
                        bigfprev = fprev;
                        bigf = f;
                    }

                    fprev = f;
                }

                fnext = sibling[bigf];

                if (fnext != Empty)
                {
                    if (bigfprev == Empty)
                    {
                        child[i] = fnext;
                    }
                    else
                    {
                        sibling[bigfprev] = fnext;
                    }

                    sibling[bigf] = Empty;
                    sibling[fprev] = bigf;
                }
            }
        }

        for (i = 0; i < nn; i++)
        {
            order[i] = Empty;
        }

        k = 0;

        for (i = 0; i < nn; i++)
        {
            if (parent[i] == Empty && nv[i] > 0)
            {
                k = PostTree(i, k, child, sibling, order, stack);
            }
        }
    }

    /// <summary>amd_post_tree: a non-recursive depth-first postorder of one tree.</summary>
    private static long PostTree(long root, long k, long[] child, long[] sibling, long[] order, long[] stack)
    {
        long f, head, h, i;

        head = 0;
        stack[0] = root;

        while (head >= 0)
        {
            i = stack[head];

            if (child[i] != Empty)
            {
                for (f = child[i]; f != Empty; f = sibling[f])
                {
                    head++;
                }

                h = head;
                for (f = child[i]; f != Empty; f = sibling[f])
                {
                    stack[h--] = f;
                }

                child[i] = Empty;
            }
            else
            {
                head--;
                order[i] = k++;
            }
        }

        return k;
    }
}
