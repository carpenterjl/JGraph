function u3_set
% U3 probe: what set(h) and set(h, name) answer for a uicontrol, a panel and a button group.
% Headless: run-probe.ps1 -Name u3_set.
f = figure('Visible', 'off');
objs = {uicontrol(f), uipanel(f), uibuttongroup(f)};
for k = 1:numel(objs)
    h = objs{k};
    s = set(h);
    fprintf('%s: set(h) is %s %s\n', class(h), class(s), mat2str(size(s)));
    names = fieldnames(s);
    for j = 1:numel(names)
        v = s.(names{j});
        if isempty(v), continue; end
        fprintf('  %s : %s\n', names{j}, v2s(v));
    end
    fprintf('  empty ones are %s %s\n', class(s.Tag), mat2str(size(s.Tag)));
end
c = objs{1};
tryp('set(c,''Tag'')', @() set(c, 'Tag'));
tryp('set(c,''Enable'')', @() set(c, 'Enable'));
tryp('set(c,''enable'')', @() set(c, 'enable'));
tryp('set(c,''Extent'')', @() set(c, 'Extent'));
tryp('set(c,''Bogus'')', @() set(c, 'Bogus'));
tryp('set(c,''TooltipString'')', @() set(c, 'TooltipString'));
tryp('set(c,''Selected'')', @() set(c, 'Selected'));
tryp('set(f,''Units'')', @() set(f, 'Units'));
tryp('set(c) nargout 0', @() evalc('set(c)'));
a = axes(f);
sa = set(a); tryp('set(axes) class', @() class(sa));
tryp('set(axes,''XScale'')', @() set(a, 'XScale'));
delete(f);
end
