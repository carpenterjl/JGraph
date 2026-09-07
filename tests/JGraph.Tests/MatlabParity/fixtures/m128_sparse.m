% M128 - sparse: the Krylov solvers and the rest of sparfun.
%
% The strong claims here are flag and iter: both are exact only if the residual norm, the
% convergence test, the re-test against b - A*x and the stagnation rule are all MATLAB's.
% relres for a converged run is not pinned as a number - it is at the level of eps and moves in
% its first figure between engines - so what is pinned is the fact it met the tolerance, which is
% what flag 0 is supposed to mean. relres IS pinned as a number wherever the run did not
% converge, because there it is a real quantity.
%
% gallery('poisson', n) answers a sparse matrix in MATLAB and is refused here, so the two test
% matrices are built from sparse(i, j, s) by the local functions at the end of the file. The
% Poisson one equals delsq(numgrid('S', n+2)) and gallery('poisson', n) exactly.

A = poissonmat(5);
n = size(A, 1);
b = ones(n, 1);
C = convdiff(4, 8);
m = size(C, 1);
bc = ones(m, 1);

fprintf('CHK|setup.poisson.size|%.17g|exact\n', n);
fprintf('CHK|setup.poisson.nnz|%.17g|exact\n', nnz(A));
fprintf('CHK|setup.poisson.sum|%.17g|exact\n', full(sum(sum(A))));
fprintf('CHK|setup.convdiff.size|%.17g|exact\n', m);
fprintf('CHK|setup.convdiff.nnz|%.17g|exact\n', nnz(C));
fprintf('CHK|setup.convdiff.sum|%.17g|rel=1e-12\n', full(sum(sum(C))));

% ---- the eleven solvers, unpreconditioned, on the symmetric positive definite Poisson matrix ---

[x1, f1, r1, i1, v1] = pcg(A, b, 1e-10, 40);
report('pcg.poisson', x1, f1, r1, i1, v1, 1e-10);
[x2, f2, r2, i2, v2] = bicg(A, b, 1e-10, 40);
report('bicg.poisson', x2, f2, r2, i2, v2, 1e-10);
[x3, f3, r3, i3, v3] = bicgstab(A, b, 1e-10, 40);
report('bicgstab.poisson', x3, f3, r3, i3, v3, 1e-10);
[x4, f4, r4, i4, v4] = bicgstabl(A, b, 1e-10, 40);
report('bicgstabl.poisson', x4, f4, r4, i4, v4, 1e-10);
[x5, f5, r5, i5, v5] = cgs(A, b, 1e-10, 40);
report('cgs.poisson', x5, f5, r5, i5, v5, 1e-10);
[x6, f6, r6, i6, v6] = minres(A, b, 1e-10, 40);
report('minres.poisson', x6, f6, r6, i6, v6, 1e-10);
[x7, f7, r7, i7, v7] = qmr(A, b, 1e-10, 40);
report('qmr.poisson', x7, f7, r7, i7, v7, 1e-10);
[x8, f8, r8, i8, v8] = symmlq(A, b, 1e-10, 40);
report('symmlq.poisson', x8, f8, r8, i8, v8, 1e-10);
[x9, f9, r9, i9, v9] = tfqmr(A, b, 1e-10, 40);
report('tfqmr.poisson', x9, f9, r9, i9, v9, 1e-10);
[x10, f10, r10, i10, v10] = lsqr(A, b, 1e-10, 40);
report('lsqr.poisson', x10, f10, r10, i10, v10, 1e-10);

% ---- the same, on the nonsymmetric convection-diffusion matrix --------------------------------

[y1, g1, s1, j1, w1] = bicg(C, bc, 1e-10, 60);
report('bicg.convdiff', y1, g1, s1, j1, w1, 1e-10);
[y2, g2, s2, j2, w2] = bicgstab(C, bc, 1e-10, 60);
report('bicgstab.convdiff', y2, g2, s2, j2, w2, 1e-10);
[y3, g3, s3, j3, w3] = bicgstabl(C, bc, 1e-10, 60);
report('bicgstabl.convdiff', y3, g3, s3, j3, w3, 1e-10);
[y4, g4, s4, j4, w4] = cgs(C, bc, 1e-10, 60);
report('cgs.convdiff', y4, g4, s4, j4, w4, 1e-10);
[y5, g5, s5, j5, w5] = qmr(C, bc, 1e-10, 60);
report('qmr.convdiff', y5, g5, s5, j5, w5, 1e-10);
[y6, g6, s6, j6, w6] = tfqmr(C, bc, 1e-10, 60);
report('tfqmr.convdiff', y6, g6, s6, j6, w6, 1e-10);
[y7, g7, s7, j7, w7] = lsqr(C, bc, 1e-10, 60);
report('lsqr.convdiff', y7, g7, s7, j7, w7, 1e-10);

% ---- runs that do not converge: here relres is a number worth pinning ------------------------

[~, fa, ra, ia, va] = pcg(A, b, 1e-14, 3);
fprintf('CHK|pcg.short.flag|%.17g|exact\n', fa);
fprintf('CHK|pcg.short.iter|%.17g|exact\n', ia);
fprintf('CHK|pcg.short.relres|%.17g|rel=1e-8\n', ra);
fprintf('CHK|pcg.short.resvec|%.17g|exact\n', numel(va));
fprintf('CHK|pcg.short.resvec1|%.17g|rel=1e-12\n', va(1));
fprintf('CHK|pcg.short.resvecend|%.17g|rel=1e-8\n', va(end));

[~, fb, rb, ib, vb] = bicgstab(A, b, 1e-14, 4);
fprintf('CHK|bicgstab.short.flag|%.17g|exact\n', fb);
fprintf('CHK|bicgstab.short.iter|%.17g|exact\n', ib);
fprintf('CHK|bicgstab.short.relres|%.17g|rel=1e-8\n', rb);
fprintf('CHK|bicgstab.short.resvec|%.17g|exact\n', numel(vb));

[~, fc2, rc2, ic2] = minres(A, b, 1e-14, 4);
fprintf('CHK|minres.short.flag|%.17g|exact\n', fc2);
fprintf('CHK|minres.short.iter|%.17g|exact\n', ic2);
fprintf('CHK|minres.short.relres|%.17g|rel=1e-8\n', rc2);

[~, fd, rd, id] = cgs(C, bc, 1e-12, 5);
fprintf('CHK|cgs.short.flag|%.17g|exact\n', fd);
fprintf('CHK|cgs.short.iter|%.17g|exact\n', id);
fprintf('CHK|cgs.short.relres|%.17g|rel=1e-8\n', rd);

% ---- gmres, restarted and not ----------------------------------------------------------------

[xg1, fg1, rg1, ig1, vg1] = gmres(A, b, [], 1e-10, 25);
fprintf('CHK|gmres.full.flag|%.17g|exact\n', fg1);
fprintf('CHK|gmres.full.iter1|%.17g|exact\n', ig1(1));
fprintf('CHK|gmres.full.iter2|%.17g|exact\n', ig1(2));
fprintf('CHK|gmres.full.converged|%.17g|exact\n', double(rg1 <= 1e-10));
fprintf('CHK|gmres.full.resvec|%.17g|exact\n', numel(vg1));
fprintf('CHK|gmres.full.x|%.17g|rel=1e-6\n', norm(xg1));
fprintf('CHK|gmres.full.residual|%.17g|abs=1e-8\n', norm(b - A*xg1));

[xg2, fg2, rg2, ig2, vg2] = gmres(A, b, 5, 1e-10, 10);
fprintf('CHK|gmres.restart5.flag|%.17g|exact\n', fg2);
fprintf('CHK|gmres.restart5.iter1|%.17g|exact\n', ig2(1));
fprintf('CHK|gmres.restart5.iter2|%.17g|exact\n', ig2(2));
fprintf('CHK|gmres.restart5.converged|%.17g|exact\n', double(rg2 <= 1e-10));
fprintf('CHK|gmres.restart5.resvec|%.17g|exact\n', numel(vg2));
fprintf('CHK|gmres.restart5.x|%.17g|rel=1e-6\n', norm(xg2));

[~, fg3, rg3, ig3, vg3] = gmres(A, b, 3, 1e-14, 2);
fprintf('CHK|gmres.short.flag|%.17g|exact\n', fg3);
fprintf('CHK|gmres.short.iter1|%.17g|exact\n', ig3(1));
fprintf('CHK|gmres.short.iter2|%.17g|exact\n', ig3(2));
fprintf('CHK|gmres.short.relres|%.17g|rel=1e-8\n', rg3);
fprintf('CHK|gmres.short.resvec|%.17g|exact\n', numel(vg3));

[xg4, fg4, rg4, ig4] = gmres(C, bc, 10, 1e-10, 6);
fprintf('CHK|gmres.convdiff.flag|%.17g|exact\n', fg4);
fprintf('CHK|gmres.convdiff.iter1|%.17g|exact\n', ig4(1));
fprintf('CHK|gmres.convdiff.iter2|%.17g|exact\n', ig4(2));
fprintf('CHK|gmres.convdiff.converged|%.17g|exact\n', double(rg4 <= 1e-10));
fprintf('CHK|gmres.convdiff.x|%.17g|rel=1e-6\n', norm(xg4));

% ---- preconditioned runs ----------------------------------------------------------------------

L = ichol(A);
[xp, fp, rp, ip, vp] = pcg(A, b, 1e-10, 40, L, L');
fprintf('CHK|pcg.ichol.flag|%.17g|exact\n', fp);
fprintf('CHK|pcg.ichol.iter|%.17g|exact\n', ip);
fprintf('CHK|pcg.ichol.converged|%.17g|exact\n', double(rp <= 1e-10));
fprintf('CHK|pcg.ichol.resvec|%.17g|exact\n', numel(vp));
fprintf('CHK|pcg.ichol.x|%.17g|rel=1e-6\n', norm(xp));

M = L * L';
[xp2, fp2, rp2, ip2] = pcg(A, b, 1e-10, 40, M);
fprintf('CHK|pcg.icholM.flag|%.17g|exact\n', fp2);
fprintf('CHK|pcg.icholM.iter|%.17g|exact\n', ip2);
fprintf('CHK|pcg.icholM.converged|%.17g|exact\n', double(rp2 <= 1e-10));

[Lu, Uu] = ilu(C);
[xi, fi, ri, ii2] = bicgstab(C, bc, 1e-10, 40, Lu, Uu);
fprintf('CHK|bicgstab.ilu.flag|%.17g|exact\n', fi);
fprintf('CHK|bicgstab.ilu.iter|%.17g|exact\n', ii2);
fprintf('CHK|bicgstab.ilu.converged|%.17g|exact\n', double(ri <= 1e-10));
fprintf('CHK|bicgstab.ilu.x|%.17g|rel=1e-6\n', norm(xi));

[xj, fj, rj, ij] = gmres(C, bc, 10, 1e-10, 6, Lu, Uu);
fprintf('CHK|gmres.ilu.flag|%.17g|exact\n', fj);
fprintf('CHK|gmres.ilu.iter1|%.17g|exact\n', ij(1));
fprintf('CHK|gmres.ilu.iter2|%.17g|exact\n', ij(2));
fprintf('CHK|gmres.ilu.converged|%.17g|exact\n', double(rj <= 1e-10));

D = spdiags(spdiags(A, 0), 0, n, n);
[xd, fd2, rd2, id2] = minres(A, b, 1e-10, 40, D);
fprintf('CHK|minres.jacobi.flag|%.17g|exact\n', fd2);
fprintf('CHK|minres.jacobi.iter|%.17g|exact\n', id2);
fprintf('CHK|minres.jacobi.converged|%.17g|exact\n', double(rd2 <= 1e-10));
fprintf('CHK|minres.jacobi.x|%.17g|rel=1e-6\n', norm(xd));

[xk, fk, rk, ik] = symmlq(A, b, 1e-10, 40, D);
fprintf('CHK|symmlq.jacobi.flag|%.17g|exact\n', fk);
fprintf('CHK|symmlq.jacobi.iter|%.17g|exact\n', ik);
fprintf('CHK|symmlq.jacobi.converged|%.17g|exact\n', double(rk <= 1e-10));

[xm2, fm2, rm2, im2] = tfqmr(A, b, 1e-10, 40, D);
fprintf('CHK|tfqmr.jacobi.flag|%.17g|exact\n', fm2);
fprintf('CHK|tfqmr.jacobi.iter|%.17g|exact\n', im2);
fprintf('CHK|tfqmr.jacobi.converged|%.17g|exact\n', double(rm2 <= 1e-10));

[xn2, fn2, rn2, in2] = qmr(A, b, 1e-10, 40, D, D);
fprintf('CHK|qmr.jacobi.flag|%.17g|exact\n', fn2);
fprintf('CHK|qmr.jacobi.iter|%.17g|exact\n', in2);
fprintf('CHK|qmr.jacobi.converged|%.17g|exact\n', double(rn2 <= 1e-10));

% ---- an initial guess, and a right-hand side of all zeros --------------------------------------

x0 = 0.1 * ones(n, 1);
[xq, fq, rq, iq] = pcg(A, b, 1e-10, 40, [], [], x0);
fprintf('CHK|pcg.x0.flag|%.17g|exact\n', fq);
fprintf('CHK|pcg.x0.iter|%.17g|exact\n', iq);
fprintf('CHK|pcg.x0.converged|%.17g|exact\n', double(rq <= 1e-10));
fprintf('CHK|pcg.x0.x|%.17g|rel=1e-6\n', norm(xq));

[xz, fz, rz, iz, vz] = pcg(A, zeros(n, 1), 1e-10, 40);
fprintf('CHK|pcg.zero.flag|%.17g|exact\n', fz);
fprintf('CHK|pcg.zero.relres|%.17g|exact\n', rz);
fprintf('CHK|pcg.zero.iter|%.17g|exact\n', iz);
fprintf('CHK|pcg.zero.x|%.17g|exact\n', norm(xz));
fprintf('CHK|pcg.zero.resvec|%.17g|exact\n', numel(vz));

exact = A \ b;
[~, fe, re, ie] = pcg(A, b, 1e-1, 10, [], [], exact);
fprintf('CHK|pcg.goodguess.flag|%.17g|exact\n', fe);
fprintf('CHK|pcg.goodguess.iter|%.17g|exact\n', ie);
fprintf('CHK|pcg.goodguess.tiny|%.17g|exact\n', double(re < 1e-10));

% ---- function-handle operands ------------------------------------------------------------------

[xh, fh, rh, ih] = pcg(@(v) A*v, b, 1e-10, 40);
fprintf('CHK|pcg.handle.flag|%.17g|exact\n', fh);
fprintf('CHK|pcg.handle.iter|%.17g|exact\n', ih);
fprintf('CHK|pcg.handle.converged|%.17g|exact\n', double(rh <= 1e-10));
fprintf('CHK|pcg.handle.same|%.17g|abs=1e-10\n', norm(xh - x1));

[xh2, fh2, rh2, ih2] = bicg(@(v, t) transposed(C, v, t), bc, 1e-10, 60);
fprintf('CHK|bicg.handle.flag|%.17g|exact\n', fh2);
fprintf('CHK|bicg.handle.iter|%.17g|exact\n', ih2);
fprintf('CHK|bicg.handle.converged|%.17g|exact\n', double(rh2 <= 1e-10));
fprintf('CHK|bicg.handle.same|%.17g|abs=1e-8\n', norm(xh2 - y1));

[xh3, fh3, rh3, ih3] = pcg(A, b, 1e-10, 40, @(v) D \ v);
fprintf('CHK|pcg.handleM.flag|%.17g|exact\n', fh3);
fprintf('CHK|pcg.handleM.iter|%.17g|exact\n', ih3);
fprintf('CHK|pcg.handleM.converged|%.17g|exact\n', double(rh3 <= 1e-10));

% ---- lsqr on a rectangular system ---------------------------------------------------------------

R = sparse([1 2 3 4 5 6 1 3 5], [1 1 2 2 3 3 3 1 2], [1 2 3 4 5 6 7 8 9], 6, 3);
rb = (1:6)';
[xr, fr, rr2, ir, vr, lv] = lsqr(R, rb, 1e-12, 20);
fprintf('CHK|lsqr.rect.flag|%.17g|exact\n', fr);
fprintf('CHK|lsqr.rect.iter|%.17g|exact\n', ir);
fprintf('CHK|lsqr.rect.resvec|%.17g|exact\n', numel(vr));
fprintf('CHK|lsqr.rect.lsvec|%.17g|exact\n', numel(lv));
fprintf('CHK|lsqr.rect.x1|%.17g|rel=1e-6\n', xr(1));
fprintf('CHK|lsqr.rect.x2|%.17g|rel=1e-6\n', xr(2));
fprintf('CHK|lsqr.rect.x3|%.17g|rel=1e-6\n', xr(3));
fprintf('CHK|lsqr.rect.residual|%.17g|rel=1e-8\n', norm(rb - R*xr));

% The initial guess has as many entries as A has columns, which for a rectangular system is not
% the length of b - the one solver here whose guess is not the size of the right-hand side.
[xr2, fr2, rr3, ir2] = lsqr(R, rb, 1e-12, 20, [], [], [1; 1; 1]);
fprintf('CHK|lsqr.rectx0.flag|%.17g|exact\n', fr2);
fprintf('CHK|lsqr.rectx0.iter|%.17g|exact\n', ir2);
fprintf('CHK|lsqr.rectx0.relres|%.17g|rel=1e-8\n', rr3);
fprintf('CHK|lsqr.rectx0.same|%.17g|abs=1e-8\n', norm(xr2 - xr));

% ---- svds ---------------------------------------------------------------------------------------

sl = svds(A, 4);
for k = 1:numel(sl)
    fprintf('CHK|svds.largest.%d|%.17g|rel=1e-10\n', k, sl(k));
end
ss = svds(A, 3, 'smallest');
for k = 1:numel(ss)
    fprintf('CHK|svds.smallest.%d|%.17g|rel=1e-10\n', k, ss(k));
end
s2 = svds(A, 3, 2);
for k = 1:numel(s2)
    fprintf('CHK|svds.near2.%d|%.17g|rel=1e-10\n', k, s2(k));
end
sr = svds(R, 2);
for k = 1:numel(sr)
    fprintf('CHK|svds.rect.%d|%.17g|rel=1e-10\n', k, sr(k));
end
[U, S, V, sf] = svds(A, 3);
fprintf('CHK|svds.U.rows|%.17g|exact\n', size(U, 1));
fprintf('CHK|svds.U.cols|%.17g|exact\n', size(U, 2));
fprintf('CHK|svds.S|%.17g|exact\n', size(S, 1));
fprintf('CHK|svds.V.rows|%.17g|exact\n', size(V, 1));
fprintf('CHK|svds.flag|%.17g|exact\n', sf);
fprintf('CHK|svds.reconstruct|%.17g|abs=1e-9\n', norm(U*S*V' - U*S*V'));
fprintf('CHK|svds.orthU|%.17g|abs=1e-10\n', norm(U'*U - eye(3)));
fprintf('CHK|svds.orthV|%.17g|abs=1e-10\n', norm(V'*V - eye(3)));
fprintf('CHK|svds.residual1|%.17g|abs=1e-9\n', norm(A*V(:,1) - S(1,1)*U(:,1)));

% ---- spdiags, all four forms ----------------------------------------------------------------------

B = [(1:6)' (11:16)' (21:26)'];
A2 = spdiags(B, [-2 0 1], 6, 6);
printmatrix('spdiags.create', full(A2));
[Bx, dx] = spdiags(A2);
printmatrix('spdiags.extract.B', Bx);
printmatrix('spdiags.extract.d', dx(:)');
printmatrix('spdiags.extractd', spdiags(A2, [0 1]));
A3 = spdiags([(1:4)' (11:14)'], [0 2], 4, 6);
printmatrix('spdiags.wide', full(A3));
A4 = spdiags([(1:4)' (11:14)'], [0 -2], 6, 4);
printmatrix('spdiags.tall', full(A4));
A5 = spdiags(-ones(6, 1), 1, A2);
printmatrix('spdiags.replace', full(A5));
printmatrix('spdiags.scalar', full(spdiags(7, [-1 1], 5, 5)));
printmatrix('spdiags.row', full(spdiags([2 3], [-1 1], 5, 5)));
printmatrix('spdiags.column', full(spdiags((1:5)', [0 2], 5, 5)));

% ---- spfun, spones, spconvert, spaugment, sprank, colperm --------------------------------------

Sp = sparse([1 2 3 1], [1 2 3 3], [1 4 9 16], 3, 3);
printmatrix('spfun', full(spfun(@sqrt, Sp)));
printmatrix('spones', full(spones(Sp)));
fprintf('CHK|spfun.nnz|%.17g|exact\n', nnz(spfun(@sqrt, Sp)));
printmatrix('spconvert', full(spconvert([1 1 2; 2 3 5; 4 2 7])));
printmatrix('spaugment', full(spaugment(R, 2)));
fprintf('CHK|spaugment.default.size|%.17g|exact\n', size(spaugment(R), 1));
fprintf('CHK|spaugment.default.c|%.17g|rel=1e-12\n', max(max(abs(full(R))))/1000);
fprintf('CHK|sprank.poisson|%.17g|exact\n', sprank(A));
fprintf('CHK|sprank.rect|%.17g|exact\n', sprank(R));
fprintf('CHK|sprank.deficient|%.17g|exact\n', sprank(sparse([1 2], [1 1], [1 1], 3, 3)));
fprintf('CHK|sprank.empty|%.17g|exact\n', sprank(sparse(4, 4)));
printmatrix('colperm', colperm(Sp));
printmatrix('colperm.poisson', colperm(A));

% ---- treelayout and gplot ------------------------------------------------------------------------

p = [3 3 6 6 6 0];
[tx, ty, th, ts] = treelayout(p);
printmatrix('treelayout.x', tx);
printmatrix('treelayout.y', ty);
fprintf('CHK|treelayout.h|%.17g|exact\n', th);
fprintf('CHK|treelayout.s|%.17g|exact\n', ts);
p2 = [2 4 2 0 6 4 6];
[t2x, t2y, t2h, t2s] = treelayout(p2);
printmatrix('treelayout2.x', t2x);
printmatrix('treelayout2.y', t2y);
fprintf('CHK|treelayout2.h|%.17g|exact\n', t2h);
fprintf('CHK|treelayout2.s|%.17g|exact\n', t2s);

Ag = sparse([1 2 3 1], [2 3 4 4], 1, 4, 4);
Ag = Ag + Ag';
xy = [0 0; 1 0; 1 1; 0 1];
[gx, gy] = gplot(Ag, xy);
fprintf('CHK|gplot.len|%.17g|exact\n', numel(gx));
printmatrix('gplot.x', gx(~isnan(gx))');
printmatrix('gplot.y', gy(~isnan(gy))');

% ---- unmesh ---------------------------------------------------------------------------------------

E = [0 0 1 0; 1 0 1 1; 1 1 0 1; 0 1 0 0; 0 0 1 1];
[Au, xyu] = unmesh(E);
fprintf('CHK|unmesh.size|%.17g|exact\n', size(Au, 1));
fprintf('CHK|unmesh.nnz|%.17g|exact\n', nnz(Au));
fprintf('CHK|unmesh.trace|%.17g|exact\n', full(sum(diag(Au))));
fprintf('CHK|unmesh.rowsum|%.17g|exact\n', max(abs(full(sum(Au, 2)))));
fprintf('CHK|unmesh.xy|%.17g|exact\n', numel(xyu));

% ---- the random constructors: only what does not depend on the stream ------------------------------

Rn = sprandn(Sp);
fprintf('CHK|sprandn.pattern.nnz|%.17g|exact\n', nnz(Rn));
fprintf('CHK|sprandn.pattern.size|%.17g|exact\n', size(Rn, 1));
fprintf('CHK|sprandn.pattern.same|%.17g|exact\n', sum(sum(abs(full(spones(Rn)) - full(spones(Sp))))));
Rd = sprandn(20, 30, 0.1);
fprintf('CHK|sprandn.size1|%.17g|exact\n', size(Rd, 1));
fprintf('CHK|sprandn.size2|%.17g|exact\n', size(Rd, 2));
fprintf('CHK|sprandn.atmost|%.17g|exact\n', double(nnz(Rd) <= 60));
Rs = sprandsym(Sp + Sp');
fprintf('CHK|sprandsym.symmetric|%.17g|exact\n', max(max(abs(full(Rs) - full(Rs)'))));
Rs2 = sprandsym(12, 0.3);
fprintf('CHK|sprandsym.size|%.17g|exact\n', size(Rs2, 1));
fprintf('CHK|sprandsym.symmetric2|%.17g|exact\n', max(max(abs(full(Rs2) - full(Rs2)'))));

% ---- refusals both engines agree about ----------------------------------------------------------

try
    pcg(sparse(3, 4), ones(3, 1));
    fprintf('CHK|pcg.nonsquare|%.17g|exact\n', 0);
catch
    fprintf('CHK|pcg.nonsquare|%.17g|exact\n', 1);
end
try
    pcg(A, ones(3, 1));
    fprintf('CHK|pcg.badrhs|%.17g|exact\n', 0);
catch
    fprintf('CHK|pcg.badrhs|%.17g|exact\n', 1);
end
try
    spdiags([1 2 3], [0 1], 4, 4);
    fprintf('CHK|spdiags.badB|%.17g|exact\n', 0);
catch
    fprintf('CHK|spdiags.badB|%.17g|exact\n', 1);
end

% ---- local functions --------------------------------------------------------------------------------

function report(name, x, flag, relres, iter, resvec, tol)
% One solver run, pinned the way the header explains.
fprintf('CHK|%s.flag|%.17g|exact\n', name, flag);
fprintf('CHK|%s.iter|%.17g|exact\n', name, iter);
fprintf('CHK|%s.converged|%.17g|exact\n', name, double(relres <= tol));
fprintf('CHK|%s.resvec|%.17g|exact\n', name, numel(resvec));
fprintf('CHK|%s.resvec1|%.17g|rel=1e-12\n', name, resvec(1));
fprintf('CHK|%s.xnorm|%.17g|rel=1e-6\n', name, norm(x));
fprintf('CHK|%s.x1|%.17g|rel=1e-6\n', name, x(1));
end

function printmatrix(name, M)
% Every entry of a small answer, so a wrong shape is as visible as a wrong value.
fprintf('CHK|%s.rows|%.17g|exact\n', name, size(M, 1));
fprintf('CHK|%s.cols|%.17g|exact\n', name, size(M, 2));
for c = 1:size(M, 2)
    for r = 1:size(M, 1)
        fprintf('CHK|%s.%d.%d|%.17g|exact\n', name, r, c, full(M(r, c)));
    end
end
end

function y = transposed(C, v, flag)
% The two directions a bicg, qmr or lsqr function handle has to answer.
if strcmp(flag, 'transp')
    y = C' * v;
else
    y = C * v;
end
end

function A = poissonmat(n)
% The 5-point negative Laplacian on an n-by-n interior grid; equals gallery('poisson', n).
N = n * n;
i = zeros(5*N, 1); j = zeros(5*N, 1); s = zeros(5*N, 1); at = 0;
for c = 1:n
    for r = 1:n
        k = (c-1)*n + r;
        at = at + 1; i(at) = k; j(at) = k; s(at) = 4;
        if r > 1, at = at + 1; i(at) = k; j(at) = k-1; s(at) = -1; end
        if r < n, at = at + 1; i(at) = k; j(at) = k+1; s(at) = -1; end
        if c > 1, at = at + 1; i(at) = k; j(at) = k-n; s(at) = -1; end
        if c < n, at = at + 1; i(at) = k; j(at) = k+n; s(at) = -1; end
    end
end
A = sparse(i(1:at), j(1:at), s(1:at), N, N);
end

function A = convdiff(n, beta)
% Centred diffusion with an upwind convection term, so the matrix is nonsymmetric but still
% diagonally dominant - the shape gmres and the bicgstab family are for.
N = n * n;
h = 1 / (n + 1);
i = zeros(5*N, 1); j = zeros(5*N, 1); s = zeros(5*N, 1); at = 0;
for c = 1:n
    for r = 1:n
        k = (c-1)*n + r;
        at = at + 1; i(at) = k; j(at) = k; s(at) = 4 + beta*h;
        if r > 1, at = at + 1; i(at) = k; j(at) = k-1; s(at) = -1 - beta*h; end
        if r < n, at = at + 1; i(at) = k; j(at) = k+1; s(at) = -1; end
        if c > 1, at = at + 1; i(at) = k; j(at) = k-n; s(at) = -1; end
        if c < n, at = at + 1; i(at) = k; j(at) = k+n; s(at) = -1; end
    end
end
A = sparse(i(1:at), j(1:at), s(1:at), N, N);
end
