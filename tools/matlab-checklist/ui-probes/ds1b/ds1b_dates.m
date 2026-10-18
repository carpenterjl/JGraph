% DS1b (designer plan): how R2025b saves a datetime, a NaT and a date picker's dates (rt/dates), read
% with ds1b_mcos.py: an MCOS datetime storing its milliseconds as data, fmt only when set. Headless.
out = fullfile(pwd, 'rt', 'dates'); if ~exist(out, 'dir'), mkdir(out); end
s = struct('one', datetime([2026 10 18]), 'nat', NaT, 'col', datetime([2026 1 1; 2026 1 2])); %#ok<NASGU>
save(fullfile(out, 'dates.mat'), 's', '-v7', '-nocompression');
fig = uifigure('Visible', 'off'); d = uidatepicker(fig);
d.Value = datetime([2026 10 18]); d.DisabledDates = datetime([2026 1 1; 2026 1 2]);
components = struct('UIFigure', fig); %#ok<NASGU>
save(fullfile(out, 'picker.mat'), 'components', '-v7', '-nocompression');
x = datetime([2026 10 18]); disp(class(x.Format)); disp(x.Format); disp(x.TimeZone);
