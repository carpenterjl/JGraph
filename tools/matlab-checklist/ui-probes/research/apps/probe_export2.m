% probe_export2.m - export a real GUIDE app (copy of the LTE one, re-saved invisible) to a single .m.
here = pwd;
diary(fullfile(here, 'probe_export2.out')); diary on
set(groot, 'DefaultFigureVisible', 'off');
src = fullfile(matlabroot, 'toolbox', 'lte', 'lte');
cp = fullfile(here, 'guide_lte_copy');
if ~isfolder(cp), mkdir(cp); end
copyfile(fullfile(src, 'saLteDLConformanceTestBenchGUI.m'), cp);
cd(cp);
f = openfig(fullfile(src, 'saLteDLConformanceTestBenchGUI.fig'), 'new', 'invisible');
hgsave(f, fullfile(cp, 'saLteDLConformanceTestBenchGUI.fig'));
delete(f);
s = load(fullfile(cp, 'saLteDLConformanceTestBenchGUI.fig'), '-mat', 'hgS_070000');
fprintf('copy saved Visible = %s\n', s.hgS_070000.properties.Visible);
if strcmp(s.hgS_070000.properties.Visible, 'off')
    out = fullfile(here, 'guide_exported', 'lteGUI_export.m');
    try
        export.internal.exportGUIDEApp(fullfile(cp, 'saLteDLConformanceTestBenchGUI.fig'), out);
        txt = fileread(out);
        fprintf('export ok: %d bytes, has LayoutFcn = %d\n', numel(txt), contains(txt, 'gui_LayoutFcn',  'IgnoreCase', false) && contains(txt, 'function h1 = '));
        fprintf('uicontrol( calls: %d\n', numel(strfind(txt, 'uicontrol(')));
    catch e
        fprintf('export err: %s | %s\n', e.identifier, e.message);
    end
end
cd(here);
delete(findall(groot, 'Type', 'figure'));
diary off
