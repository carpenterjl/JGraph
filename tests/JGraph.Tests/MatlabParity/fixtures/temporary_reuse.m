% temporary_reuse.m -- Z2 of the value-ownership plan (ADR 0173): an operator that writes its
% answer into a fresh temporary operand (Z2a), and v = v op E written into v's own storage (Z2b),
% answer the bits the allocating roads answer, and every alias, capture and earlier reader keeps
% the value it had. The arrays are 70,000 elements, above the reuse roads' floor (65,536); every
% line prints a digest of the bits (helpers/tr_bits.m), so -0, NaN and a subnormal count. The
% operands are made by exact integer arithmetic and one rounding each, never by a transcendental,
% whose vector kernel is within ulps of MATLAB's above 32K elements by design (ADR 0093).
n = 70000;
A = mod((1:n) * 7919, 1009) / 13 - 40;
B = mod((1:n) * 104729, 997) / 7 + 2;
s = 0.37;
v0 = (1:n) / 7;

% --- v = v op E and v = E op v, every operator the in-place road takes
v = v0; v = v - s * A;   fprintf('CHK|v_minus_sA|%s|exact\n', tr_bits(v));
v = v0; v = v - A * s;   fprintf('CHK|v_minus_As|%s|exact\n', tr_bits(v));
v = v0; v = v + A * s;   fprintf('CHK|v_plus_As|%s|exact\n', tr_bits(v));
v = v0; v = v + s .* A;  fprintf('CHK|v_plus_sA|%s|exact\n', tr_bits(v));
v = v0; v = v - A * B(1) - s * A;  fprintf('CHK|v_two_products|%s|exact\n', tr_bits(v));
M = reshape(v0, 700, 100); N = reshape(A, 700, 100);
M = M - N * s;           fprintf('CHK|M_minus_Ns|%s|exact\n', tr_bits(M));
M = M + s * N;           fprintf('CHK|M_plus_sN|%s|exact\n', tr_bits(M));
M = M - N .* 2;          fprintf('CHK|M_minus_N2|%s|exact\n', tr_bits(M));
fprintf('CHK|N_kept|%s|exact\n', tr_bits(N));
v = v0; v = v + A;       fprintf('CHK|v_plus_A|%s|exact\n', tr_bits(v));
v = v0; v = v .* B;      fprintf('CHK|v_times_B|%s|exact\n', tr_bits(v));
v = v0; v = v ./ B;      fprintf('CHK|v_over_B|%s|exact\n', tr_bits(v));
v = v0; v = v * s;       fprintf('CHK|v_scaled|%s|exact\n', tr_bits(v));
v = v0; v = v / s;       fprintf('CHK|v_divided|%s|exact\n', tr_bits(v));
v = v0; v = v + s;       fprintf('CHK|v_plus_s|%s|exact\n', tr_bits(v));
v = v0; v = v - s;       fprintf('CHK|v_minus_s|%s|exact\n', tr_bits(v));
v = v0; v = A - v;       fprintf('CHK|A_minus_v|%s|exact\n', tr_bits(v));
v = v0; v = s - v;       fprintf('CHK|s_minus_v|%s|exact\n', tr_bits(v));
v = v0; v = s ./ v;      fprintf('CHK|s_over_v|%s|exact\n', tr_bits(v));
v = v0; v = s * v;       fprintf('CHK|s_times_v|%s|exact\n', tr_bits(v));
v = v0; v = A .* v;      fprintf('CHK|A_times_v|%s|exact\n', tr_bits(v));
fprintf('CHK|v0_kept|%s|exact\n', tr_bits(v0));

% --- fresh temporaries reused by the enclosing operator (Z2a)
w = (v0 - A) * s;                    fprintf('CHK|w_temp_scaled|%s|exact\n', tr_bits(w));
w = (s - A) .* B;                    fprintf('CHK|w_scalar_left_temp|%s|exact\n', tr_bits(w));
w = ((v0 - A) * s + B) ./ (A + 1);   fprintf('CHK|w_three_deep|%s|exact\n', tr_bits(w));
w = -(v0 - A) + B;                   fprintf('CHK|w_negated_temp|%s|exact\n', tr_bits(w));
w = (v0 - A)' * s;                   fprintf('CHK|w_transposed_temp|%s|exact\n', tr_bits(w));
w = v0 - (A - B) - (A .* B);         fprintf('CHK|w_right_temps|%s|exact\n', tr_bits(w));
w = (v0 + A) + (B + A) + (v0 .* B);  fprintf('CHK|w_plus_chain|%s|exact\n', tr_bits(w));
w = (v0 - A) - (v0 - A);             fprintf('CHK|w_same_temps|%s|exact\n', tr_bits(w));
fprintf('CHK|A_kept|%s|exact\n', tr_bits(A));
fprintf('CHK|B_kept|%s|exact\n', tr_bits(B));

% --- aliases, captures and earlier readers keep their value
w = v0; v = v0; v = v - A;
fprintf('CHK|alias_kept|%s|exact\n', tr_bits(w));
fprintf('CHK|v_after_alias|%s|exact\n', tr_bits(v));
f = @() v; v = v - A;
fprintf('CHK|capture_kept|%s|exact\n', tr_bits(f()));
fprintf('CHK|v_after_capture|%s|exact\n', tr_bits(v));
c = {v}; v = v + 1;
fprintf('CHK|cell_kept|%s|exact\n', tr_bits(c{1}));
fprintf('CHK|v_after_cell|%s|exact\n', tr_bits(v));
st.f = v; v = v * 2;
fprintf('CHK|field_kept|%s|exact\n', tr_bits(st.f));
fprintf('CHK|v_after_field|%s|exact\n', tr_bits(v));
u = v; u = u - 1; u = u - 1;
fprintf('CHK|v_after_alias_update|%s|exact\n', tr_bits(v));
fprintf('CHK|u_twice|%s|exact\n', tr_bits(u));

% --- a global read and bumped in the same expression (tr_bump adds 2 to gv and answers 1)
global gv;
gv = v0; out = gv + tr_bump();
fprintf('CHK|out_hold|%s|exact\n', tr_bits(out));
fprintf('CHK|gv_bumped|%s|exact\n', tr_bits(gv));
gv = v0; out = plus(gv, tr_bump());
fprintf('CHK|out_plus_hold|%s|exact\n', tr_bits(out));
gv = v0; out = [gv, tr_bump()];
fprintf('CHK|out_concat_hold|%s|exact\n', tr_bits(out));
gv = v0; gv = gv + tr_bump();
fprintf('CHK|gv_self_bump|%s|exact\n', tr_bits(gv));
gv = v0; gv = tr_bump() + gv;
fprintf('CHK|gv_self_bump_right|%s|exact\n', tr_bits(gv));

% --- ans as the updated name, while a cell still holds the payload
C = {v0}; C{1}; ans = ans - A;
fprintf('CHK|ans_updated|%s|exact\n', tr_bits(ans));
fprintf('CHK|C_kept|%s|exact\n', tr_bits(C{1}));

% --- the 462-step update loops
zz = v0; for i = 1:462, zz = zz - A * (s * i); end
fprintf('CHK|loop_zz|%s|exact\n', tr_bits(zz));
zz = v0; for i = 1:462, zz = zz - tr_scaled(A, i); end
fprintf('CHK|loop_call|%s|exact\n', tr_bits(zz));
zz = v0; for i = 1:462, zz = tr_scaled(A, i) - zz; end
fprintf('CHK|loop_call_right|%s|exact\n', tr_bits(zz));
kk = (1:462) * 1e-3; r = v0; for i = 1:462, r = r - A * kk(i); end
fprintf('CHK|loop_indexed_scale|%s|exact\n', tr_bits(r));
fprintf('CHK|v0_kept_after_loops|%s|exact\n', tr_bits(v0));

% --- NaN, the infinities, negative zero and a subnormal
q = v0; q(1:6) = [NaN Inf -Inf -0 1e-310 0];
q = q - 0;      fprintf('CHK|q_minus_zero|%s|exact\n', tr_bits(q));
q = q + 0;      fprintf('CHK|q_plus_zero|%s|exact\n', tr_bits(q));
q(1) = 1;
p = q * -1;     fprintf('CHK|p_negated|%s|exact\n', tr_bits(p));
p = 0 - q;      fprintf('CHK|p_zero_minus|%s|exact\n', tr_bits(p));
p = q * 1e-300; fprintf('CHK|p_underflow|%s|exact\n', tr_bits(p));
p = q; p = p .* B; p = p ./ B;
fprintf('CHK|p_round_trip|%s|exact\n', tr_bits(p));
fprintf('CHK|q_kept|%s|exact\n', tr_bits(q));
