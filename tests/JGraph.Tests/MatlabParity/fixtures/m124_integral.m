% m124_integral.m -- the quadrature that exists today: integral, quadgk, trapz, cumtrapz.
% Tolerances follow what each call promises: the default integral asks for 1e-6 relative and
% 1e-10 absolute, so a smooth integrand lands well inside 1e-10 and is pinned there; a call with
% its own RelTol is pinned at that RelTol.

% Smooth, finite.
fprintf('CHK|poly|%.17g|rel=1e-12\n', integral(@(x) x.^2, 0, 1));
fprintf('CHK|sinx|%.17g|rel=1e-10\n', integral(@(x) sin(x), 0, pi));
fprintf('CHK|expx2|%.17g|rel=1e-10\n', integral(@(x) exp(-x.^2), 0, 2));

% Infinite and half-infinite ranges are folded onto a finite one.
fprintf('CHK|gauss_full|%.17g|rel=1e-10\n', integral(@(x) exp(-x.^2), -Inf, Inf));
fprintf('CHK|expdecay_half|%.17g|rel=1e-10\n', integral(@(x) exp(-x), 0, Inf));
fprintf('CHK|lorentz|%.17g|rel=1e-10\n', integral(@(x) 1 ./ (1 + x.^2), -Inf, Inf));

% Reversed limits change the sign; equal limits answer zero.
fprintf('CHK|reversed|%.17g|rel=1e-12\n', integral(@(x) x.^2, 1, 0));
fprintf('CHK|degenerate|%.17g|exact\n', integral(@(x) x.^2, 3, 3));

% Options: a stated RelTol is the promise being measured.
fprintf('CHK|reltol|%.17g|rel=1e-8\n', integral(@(x) sqrt(x), 0, 1, 'RelTol', 1e-8, 'AbsTol', 1e-12));

% An integrable endpoint singularity that the Gauss-Kronrod rule handles.
fprintf('CHK|sqrt_sing|%.17g|rel=1e-8\n', integral(@(x) 1 ./ sqrt(x), 0, 1));

% A strongly singular integrand: recorded divergence (ADR 0123). MATLAB's integral answers 9.9934,
% JGraph 9.79, the true value is 10 -- and neither is within its tolerance. The rule says the two
% must differ; if they ever agree, the ADR's line is retired.
fprintf('CHK|strong_sing|%.17g|div=ADR0123\n', integral(@(x) x.^-0.9, 0, 1));

% quadgk is the same engine, and answers errbnd beside the value.
[q, errbnd] = quadgk(@(x) sin(x) ./ x, 0, 10);
fprintf('CHK|quadgk_value|%.17g|rel=1e-10\n', q);
fprintf('CHK|quadgk_errbnd_small|%d|exact\n', double(errbnd < 1e-8));
fprintf('CHK|quadgk_inf|%.17g|rel=1e-10\n', quadgk(@(x) exp(-x.^2), -Inf, Inf));

% Oscillatory: many periods test the subdivision, not the rule.
fprintf('CHK|oscillatory|%.17g|abs=1e-9\n', integral(@(x) cos(50 * x), 0, 2 * pi));

% trapz and cumtrapz are arithmetic, so they are pinned to rounding.
x = 0:0.1:1;
fprintf('CHK|trapz|%.17g|rel=1e-14\n', trapz(x, x.^2));
fprintf('CHK|trapz_unit|%.17g|rel=1e-14\n', trapz([1 4 9 16]));
c = cumtrapz(x, x.^2);
fprintf('CHK|cumtrapz_shape|%s|shape\n', mat2str(size(c)));
fprintf('CHK|cumtrapz_end|%.17g|rel=1e-14\n', c(end));
fprintf('CHK|cumtrapz_mid|%.17g|rel=1e-14\n', c(6));
