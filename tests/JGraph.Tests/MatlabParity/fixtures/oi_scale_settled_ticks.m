% record: -noFigureWindows
% Open items 49 and 64 (ADR 0236), in the MATLAB dialect: the automatic ticks a scale settles on —
% a slider's over its track, a knob's over its label ring, a gauge's over its arc — read after the
% view has settled (R2025b re-ticks some time after a drawnow; this build answers at once), the
% knob's outer rectangle from its label ring, the silent aspect coercion of a Position write, and
% the numeric field's refusal of a ValueChangingFcn. Lines marked div=ADR0236 are where R2025b's
% view walks a rounded step of its own that no equal division reaches, or where the measured hull
% sits a few pixels from its box.
uf = uifigure('Visible', 'off', 'Position', [100 100 900 620]);
warm = uislider(uf, 'Position', [10 10 150 3]);
drawnow; pause(2); delete(warm);

s = cell(1, 12);
lims = {[0 100], [0 7], [0 255], [0 1000], [-5 5], [0 0.5], [1 12], [0 1e6], [0.01 0.99]};
for k = 1:numel(lims)
    s{k} = uislider(uf, 'Position', [10, 10 + 44 * k, 150, 3], 'Limits', lims{k});
end
s{10} = uislider(uf, 'Position', [500 54 60 3]);
s{11} = uislider(uf, 'Position', [500 98 100 3], 'Limits', [0 1]);
s{12} = uislider(uf, 'Position', [500 142 300 3]);
kn = uiknob(uf, 'Position', [520 300 60 60]);
kn2 = uiknob(uf, 'Position', [640 300 60 60], 'Limits', [0 1000]);
kn3 = uiknob(uf, 'Position', [520 420 120 120]);
kn4 = uiknob(uf, 'Position', [680 440 60 60], 'Limits', [0 1]);
ga = uigauge(uf, 'Position', [20 440 120 120]);
ni = uigauge(uf, 'ninetydegree', 'Position', [160 440 90 90]);
se = uigauge(uf, 'semicircular', 'Position', [280 460 120 65]);
li = uigauge(uf, 'linear', 'Position', [280 420 120 40]);
t0 = tic;
while toc(t0) < 4, drawnow; pause(0.25); end
drawnow;

tix = @(h) {h.MajorTicks, h.MajorTickLabels, numel(h.MinorTicks)};
u9b_chk('sl_100', @() tix(s{1}));
u9b_chk('sl_7', @() tix(s{2}));
u9b_chkdiv('sl_255', @() oi_try( @() tix(s{3})), 'ADR0236');
u9b_chkdiv('sl_1000', @() oi_try( @() tix(s{4})), 'ADR0236');
u9b_chk('sl_pm5', @() tix(s{5}));
u9b_chk('sl_half', @() tix(s{6}));
u9b_chkdiv('sl_1_12', @() oi_try( @() tix(s{7})), 'ADR0236');
u9b_chkdiv('sl_1e6', @() oi_try( @() tix(s{8})), 'ADR0236');
u9b_chkdiv('sl_099', @() oi_try( @() tix(s{9})), 'ADR0236');
u9b_chk('sl_w60', @() tix(s{10}));
u9b_chk('sl_w100_01', @() tix(s{11}));
u9b_chk('sl_w300', @() tix(s{12}));
u9b_chk('kn_100', @() tix(kn));
u9b_chkdiv('kn_1000', @() oi_try( @() tix(kn2)), 'ADR0236');
u9b_chkdiv('kn_big', @() oi_try( @() tix(kn3)), 'ADR0236');
u9b_chkdiv('kn_01', @() oi_try( @() tix(kn4)), 'ADR0236');
u9b_chk('ga_100', @() tix(ga));
u9b_chk('ni_100', @() tix(ni));
u9b_chk('se_100', @() tix(se));
u9b_chk('li_100', @() tix(li));

% The outer rectangle the knob's label ring takes: the sides agree, the vertical reach is a few
% pixels its own (probe u9_settle).
u9b_chkdiv('kn_outer', @() oi_try( @() round(kn.OuterPosition)), 'ADR0236');
u9b_chkdiv('dk_outer', @() oi_try( @() round(get(uiknob(uf, 'discrete', 'Position', [200 200 60 60]), 'OuterPosition'))), 'ADR0236');
u9b_chkdiv('sw_outer', @() oi_try( @() round(get(uiswitch(uf, 'Position', [200 100 45 20]), 'OuterPosition'))), 'ADR0236');

% A Position write with the wrong aspect is coerced silently, with no warning (probes u9_settle,
% u9w_events).
k5 = uiknob(uf); lastwarn('', ''); k5.Position = [10 10 100 30]; drawnow;
u9b_chk('kn_aspect', @() {k5.Position, lastwarn});
w5 = uiswitch(uf); lastwarn('', ''); w5.Position = [10 10 100 10]; drawnow;
u9b_chk('sw_aspect', @() {w5.Position, lastwarn});
m5 = uigauge(uf, 'semicircular'); lastwarn('', ''); m5.Position = [10 10 100 30]; drawnow;
u9b_chk('se_aspect', @() {m5.Position, lastwarn});

% A numeric field has no ValueChangingFcn (probe u5w_events): set's refusal here, the dot write's
% in OpenItemsBatch23Tests.
nf = uieditfield(uf, 'numeric');
u9b_chk('nf_changing', @() set(nf, 'ValueChangingFcn', @(a, b) 0));
delete(uf);

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end
