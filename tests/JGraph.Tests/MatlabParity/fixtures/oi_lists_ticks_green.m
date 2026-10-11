% record: -noFigureWindows
% Open items 96, 97 and 98 (ADR 0225): the colour letter 'g' and the name 'green'; a ruler's tick
% labels written over its Exponent, its TickLabelFormat as R2025b spells, checks and writes it, and
% R2025b's automatic labels; a comma-separated list where one value is wanted, by where it stands,
% and spread into subscripts and a colon. Probes probe_96, probe_97, probe_97b, probe_97c, probe_98,
% probe_98b and probe_98c (open-items scratch). The ticks are set by hand: which ticks R2025b
% chooses is a separate open item.
close all force

% --- item 96: green
f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
u9b_chk('green_spec', @() get(plot(ax, 1:2, '-g'), 'Color'));
u9b_chk('green_letter', @() get(line(ax, 1:2, 1:2, 'Color', 'g'), 'Color'));
u9b_chk('green_name', @() get(line(ax, 1:2, 1:2, 'Color', 'green'), 'Color'));
u9b_chk('green_string', @() get(line(ax, 1:2, 1:2, 'Color', "green"), 'Color'));
u9b_chk('green_marker', @() get(plot(ax, 1:2, 'gs', 'MarkerFaceColor', 'g'), 'MarkerFaceColor'));
u9b_chk('green_patch', @() get(patch(ax, [0 1 1], [0 0 1], 'g'), 'FaceColor'));
u9b_chk('green_text', @() get(text(ax, 0, 0, 'x', 'Color', 'g'), 'Color'));
u9b_chk('green_bar', @() get(bar(ax, 1:2, 'g'), 'FaceColor'));
u9b_chk('green_validatecolor', @() validatecolor('g'));
u9b_chk('other_letters', @() cellfun(@(c) get(line(ax, 1:2, 1:2, 'Color', c), 'Color'), {'b', 'r', 'c', 'm', 'y', 'k', 'w'}, 'UniformOutput', false));
close all force

% --- item 97: labels over the exponent, and the format
f = figure('Visible', 'off');
ax = axes(f);
plot(ax, [0 50000], [0 1]);
ax.XLim = [0 50000];
ax.XTick = 0:10000:50000;
u9b_chk('exp_auto', @() {ax.XAxis.Exponent, class(ax.XAxis.Exponent), ax.XAxis.ExponentMode, ax.XTickLabel'});
ax.XAxis.Exponent = 3;
u9b_chk('exp_three', @() {ax.XAxis.ExponentMode, ax.XTickLabel'});
ax.XAxis.Exponent = 0;
u9b_chk('exp_zero', @() ax.XTickLabel');
ax.XAxis.ExponentMode = 'auto';
ax.XTick = [0 12345 50000];
u9b_chk('exp_uneven', @() {ax.XAxis.Exponent, ax.XTickLabel'});
ax.XTick = [0 12345.678 50000];
u9b_chk('exp_long', @() ax.XTickLabel');
ax.XTickLabel = {'a', 'b', 'c'};
u9b_chk('exp_labels_given', @() {ax.XAxis.Exponent, ax.XTickLabel'});
ax.XTickLabelMode = 'auto';
ax.XLim = [-0.0003 0.0007];
ax.XTick = -0.0003:0.0001:0.0007;
u9b_chk('exp_small', @() {ax.XAxis.Exponent, ax.XTickLabel'});
ax.XLim = [123456 123460];
ax.XTick = 123456:0.5:123460;
u9b_chk('exp_offset', @() {ax.XAxis.Exponent, ax.XTickLabel'});
ax.XLim = [0 7];
ax.XTick = [0 1/3 2 7];
u9b_chk('auto_third', @() ax.XTickLabel');
ax.XLim = [0 2];
ax.XTick = [0 0.25 0.5 1.75];
u9b_chk('auto_quarters', @() ax.XTickLabel');
u9b_chk('format_default', @() {ax.XAxis.TickLabelFormat, xtickformat(ax), ytickformat(ax)});
ax.XTick = [0 1 2];
formats = {'%.1f', 'usd', 'eur', 'gbp', 'jpy', 'degrees', 'percentage', '%,.2f', '%+g', '%d', '%.0e', '%5.1f', 'x = %g m', '%%%g', '%G', '%i'};
for k = 1:numel(formats)
    xtickformat(ax, formats{k});
    u9b_chk(sprintf('format_%d', k), @() {formats{k}, ax.XAxis.TickLabelFormat, ax.XTickLabel'});
end
u9b_chk('format_property_words', @() {as_statement(@() set_format(ax.XAxis, 'auto')), as_statement(@() set_format(ax.XAxis, 'usd')), as_statement(@() set_format(ax.XAxis, 'degrees'))});
xtickformat(ax, 'auto');
u9b_chk('format_auto_again', @() ax.XAxis.TickLabelFormat);
ax.XLim = [0 3000];
ax.XTick = [0 1500 3000];
ax.XAxis.TickLabelFormat = '%,.2f';
u9b_chk('format_grouping', @() ax.XTickLabel');
ax.XLim = [0 50000];
ax.XTick = 0:10000:50000;
ax.XAxis.TickLabelFormat = '%.2f';
u9b_chk('format_over_exponent', @() {ax.XAxis.Exponent, ax.XTickLabel'});
bad = {'%s', '%c', 'hello', '%'};
for k = 1:numel(bad)
    u9b_chk(sprintf('format_refused_%d', k), @() as_statement(@() set_format(ax.XAxis, bad{k})));
end
ax.XAxis.TickLabelFormat = '%g';
ax.XScale = 'log';
ax.XLim = [1 1e5];
ax.XTick = 10.^(0:5);
u9b_chk('log_decades', @() ax.XTickLabel');
pax = polaraxes(f);
u9b_chk('polar_formats', @() {pax.RAxis.TickLabelFormat, rtickformat(pax), pax.ThetaAxis.TickLabelFormat, thetatickformat(pax)});
close all force

% --- item 98: a comma-separated list where one value is wanted
u = struct('f', {1, 2});
s = struct('f', {});
c2 = {1, 2};
c0 = {};
u9b_chk('dot_assign_first', @() run_text('v = u.f;', u, s, c2, c0));
u9b_chk('dot_assign_none', @() run_text('v = s.f;', u, s, c2, c0));
u9b_chk('brace_assign_first', @() run_text('v = c2{:};', u, s, c2, c0));
u9b_chk('brace_assign_none', @() run_text('v = c0{:};', u, s, c2, c0));
u9b_chk('paren_first', @() run_text('v = (u.f);', u, s, c2, c0));
u9b_chk('nested_first', @() run_text('w = struct(''g'', {u}); v = w.g.f;', u, s, c2, c0));
u9b_chk('deep_first', @() run_text('w = struct(''g'', {u, u}); v = w(1).g.f;', u, s, c2, c0));
u9b_chk('operator_many', @() run_text('v = u.f + 1;', u, s, c2, c0));
u9b_chk('operator_none', @() run_text('v = s.f + 1;', u, s, c2, c0));
u9b_chk('unary_many', @() run_text('v = -c2{:};', u, s, c2, c0));
u9b_chk('unary_none', @() run_text('v = ~s.f;', u, s, c2, c0));
u9b_chk('transpose_many', @() run_text('v = u.f'';', u, s, c2, c0));
u9b_chk('if_many', @() run_text('if u.f, v = 1; else, v = 0; end', u, s, c2, c0));
u9b_chk('if_none', @() run_text('if c0{:}, v = 1; else, v = 0; end', u, s, c2, c0));
u9b_chk('while_none', @() run_text('v = 0; while s.f, v = 1; end', u, s, c2, c0));
u9b_chk('switch_many', @() run_text('switch c2{:}, case 1, v = 1; otherwise, v = 2; end', u, s, c2, c0));
u9b_chk('for_many', @() run_text('v = 0; for q = u.f, v = v + q; end', u, s, c2, c0));
u9b_chk('andand_many', @() run_text('v = u.f && true;', u, s, c2, c0));
u9b_chk('oror_none', @() run_text('v = c0{:} || true;', u, s, c2, c0));
u9b_chk('index_after_many', @() run_text('v = u.f(1);', u, s, c2, c0));
u9b_chk('index_after_none', @() run_text('v = s.f(1);', u, s, c2, c0));
u9b_chk('brace_after_many', @() run_text('cc = {{1}, {2}}; v = cc{:}{1};', u, s, c2, c0));
u9b_chk('dot_after_none', @() run_text('w = struct(''g'', {}); v = w.g.f;', u, s, c2, c0));
u9b_chk('colon_spread', @() run_text('v = 1:u.f;', u, s, c2, c0));
u9b_chk('colon_spread_brace', @() run_text('v = c2{:}:3;', u, s, c2, c0));
u9b_chk('colon_none', @() run_text('v = 1:s.f;', u, s, c2, c0));
u9b_chk('subscripts_spread', @() run_text('m = [10 20; 30 40]; v = m(u.f);', u, s, c2, c0));
u9b_chk('subscripts_brace', @() run_text('A = magic(3); idx = {2, '':''}; v = A(idx{:});', u, s, c2, c0));
u9b_chk('subscripts_none', @() run_text('m = [10 20 30]; v = m(s.f);', u, s, c2, c0));
u9b_chk('write_spread', @() run_text('A = magic(3); idx = {2, '':''}; A(idx{:}) = 0; v = A;', u, s, c2, c0));
u9b_chk('write_spread_dot', @() run_text('v = [10 20; 30 40]; v(u.f) = 5;', u, s, c2, c0));
u9b_chk('write_none', @() run_text('v = [10 20 30]; v(s.f) = 5;', u, s, c2, c0));
u9b_chk('char_colon', @() run_text('A = magic(3); v = A('':'')'';', u, s, c2, c0));
u9b_chk('anon_first', @() run_text('g = @() u.f; v = g();', u, s, c2, c0));
u9b_chk('anon_none', @() run_text('g = @() s.f; v = g();', u, s, c2, c0));
u9b_chk('function_first', @() first_of(u));
u9b_chk('function_none', @() as_statement(@() first_of(s)));
u9b_chk('function_brace_none', @() as_statement(@() first_brace(c0)));
u9b_chk('bare_none', @() bare_none(s));
u9b_chk('spread_still', @() {[u.f], {u.f}, numel({s.f}), size([s.f])});
u9b_chk('findprop_none', @() run_text('figure(''Visible'', ''off''); h = line(1:2, 1:2); v = findprop(h, ''Bogus'').Name;', u, s, c2, c0));
u9b_chk('struct_empty_shapes', @() {size(struct('f', cell(0, 3))), size(struct('f', cell(3, 0))), size(struct('f', {})), size(struct('f', cell(1, 0)))});
close all force

function set_format(ruler, format)
ruler.TickLabelFormat = format;
end

function r = run_text(code, u, s, c2, c0) %#ok<INUSL>
% The value v holds after the statements, or the refusal's identifier and sentence.
try
    eval(code);
    r = v;
catch err
    r = [err.identifier ' | ' err.message];
end
end

function y = first_of(t)
y = t.f;
end

function y = first_brace(c)
y = c{:};
end

function r = bare_none(s)
ans = 'untouched'; %#ok<NOANS>
shown = evalc('s.f');
r = {shown, ans}; %#ok<NOANS>
end

function r = as_statement(fn)
% 'ran', or the refusal's identifier and sentence.
try
    fn();
    r = 'ran';
catch err
    r = [err.identifier ' | ' err.message];
end
end
