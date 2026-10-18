% DS1b (designer plan): what setting a property changes in a saved button (rt/setsaves), against a
% fresh one, read with ds1b_mcos.py; ds1b_rules.py starts from these. Headless.
out = fullfile(pwd, 'rt', 'setsaves'); if ~exist(out, 'dir'), mkdir(out); end
cases = {'fresh', {}; 'bg', {'BackgroundColor', [1 0 0]}; 'fc', {'FontColor', [0 1 0]}; 'text', {'Text', 'Hello'}; 'fs', {'FontSize', 20}; 'cb', {'ButtonPushedFcn', 'GoPushed'}};
for k = 1:size(cases, 1)
    fig = uifigure('Visible', 'off');
    b = uibutton(fig, 'push');
    if ~isempty(cases{k, 2}), set(b, cases{k, 2}{:}); end
    components = struct('UIFigure', fig); %#ok<NASGU>
    save(fullfile(out, ['button__' cases{k, 1} '.mat']), 'components', '-v7', '-nocompression');
    delete(fig);
end
