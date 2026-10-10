% Open item 66 (ADR 0220): a datetime outside the years .NET holds (0, before 0, 10000 and on) reads
% its year, month, day, weekday and day of the year, takes a new year, and moves to the start of its
% month; and a format a script set is kept through a write and a concatenation, where a default one
% is worked out again from the moments. Probes probe_66 and probe_66b (open-items scratch).

z = datetime(0, 1, 1);
u9b_chk('year_zero', @() {char(z), z.Year, z.Month, z.Day, weekday(z), day(z, 'dayofyear')});
far = datetime(10000, 3, 4, 5, 6, 7);
u9b_chk('year_10000', @() {char(far), far.Year, far.Month, far.Day, far.Hour, far.Minute, far.Second, weekday(far)});
bc = datetime(-50, 6, 15);
u9b_chk('year_minus_50', @() {char(bc), year(bc), month(bc), day(bc), weekday(bc), day(bc, 'dayofyear')});
big = datetime(12345, 12, 31);
u9b_chk('year_12345', @() {char(big), year(big), quarter(big), week(big), weekday(big)});
moved = datetime(10000, 1, 1);
moved.Year = 20000;
u9b_chk('set_year', @() {char(moved), moved.Year});
u9b_chk('ymd_leap', @() ymd_row(datetime(10000, 2, 29)));
u9b_chk('datevec_far', @() datevec(far));
u9b_chk('yyyymmdd_far', @() yyyymmdd(far));
u9b_chk('dateshift_far', @() char(dateshift(far, 'start', 'month')));
u9b_chk('timeofday_far', @() char(timeofday(far)));
u9b_chk('diff_far', @() char(datetime(10000, 1, 1) - datetime(9999, 1, 1)));

% day and week by kind: R2025b's week is the week of the year, Sunday first, not ISO's
ds = datetime([2026 2026 2026 2021 2021 2021 2024 2023], [1 1 12 1 1 1 12 1], [1 4 31 1 2 3 31 7]);
u9b_chk('week_default', @() week(ds));
u9b_chk('week_of_month', @() week(ds, 'weekofmonth'));
u9b_chk('week_iso', @() week(ds, 'iso-weekofyear'));
u9b_chk('week_iso_of_month', @() week(datetime(2026, 1, 1) + caldays(0:40), 'iso-weekofmonth'));
u9b_chk('day_of_year', @() day(ds, 'dayofyear'));
u9b_chk('day_of_week', @() day(ds, 'dayofweek'));
u9b_chk('day_iso_of_week', @() day(ds(1:7), 'iso-dayofweek'));
u9b_chk('day_names', @() {day(ds(1:2), 'name'), day(ds(1:2), 'shortname')});
u9b_chk('day_bad_kind', @() day(ds, 'bogus'));
u9b_chk('week_bad_kind', @() week(ds, 'bogus'));
u9b_chk('dateshift_default_format', @() dateshift(datetime(2020, 1, 2, 3, 4, 5), 'start', 'month').Format);

a = datetime(2020, 1, 2);
b = datetime(2020, 1, 2, 3, 4, 5);
y = datetime(2020, 1, 2);
y.Format = 'yyyy';
u9b_chk('cat_default', @() fmt([a b]));
u9b_chk('cat_default_then_set', @() fmt([a y]));
u9b_chk('cat_timed_then_set', @() fmt([b y]));
u9b_chk('cat_set_then_timed', @() fmt([y b]));
s = datetime(2020, 1, 2);
s.Format = 'dd-MMM-uuuu';
u9b_chk('cat_set_default_text', @() fmt([s b]));
c = datetime(2020, 1, 2, 'Format', 'dd-MMM-uuuu');
u9b_chk('cat_ctor_format', @() fmt([c b]));
d = datetime(2020, 1, 2);
d.Format = 'default';
u9b_chk('format_default_word', @() {d.Format, fmt([d b])});
u9b_chk('format_empty', @() set_format(datetime(2020, 1, 2), ''));
w = datetime(2020, 1, 2);
w.Format = 'dd-MMM-uuuu';
w(2) = b;
u9b_chk('write_keeps_set', @() w.Format);
v = datetime(2020, 1, 2);
v(2) = b;
u9b_chk('write_default', @() v.Format);
t = datetime(2020, 1, 2);
t.Format = 'dd-MMM-uuuu HH:mm:ss';
t(2) = datetime(2020, 1, 3);
u9b_chk('write_midnight_keeps_set', @() t.Format);

function r = ymd_row(d)
[y, m, dd] = ymd(d);
r = [y m dd];
end

function f = fmt(x)
f = x.Format;
end

function d = set_format(d, f)
d.Format = f;
end
