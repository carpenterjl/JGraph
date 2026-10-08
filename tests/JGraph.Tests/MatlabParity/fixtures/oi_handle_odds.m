% Open items 83 and 84 (ADR 0218): delete of a handle that names nothing any more is quiet, delete of a
% number that never was one is MATLAB:hg:udd_interface:CannotDelete, and close of either is
% MATLAB:close:InvalidFigureHandle. With no window, printdlg, printpreview and exportsetupdlg are
% R2025b's blocking-dialog refusal. Every figure here stays invisible. Each call is a statement, asked
% for nothing. Probe probe_b8 (open-items scratch).

f = figure('Visible', 'off');
close(f);
u9b_chk('delete_deleted_figure', @() as_statement(@() delete(f)));
u9b_chk('close_deleted_figure', @() as_statement(@() close(f)));
u9b_chk('ishghandle_deleted', @() ishghandle(f));
u = uifigure('Visible', 'off');
delete(u);
u9b_chk('delete_deleted_uifigure', @() as_statement(@() delete(u)));
p = plot(axes(figure('Visible', 'off')), 1:3);
delete(p);
u9b_chk('delete_deleted_line', @() as_statement(@() delete(p)));
u9b_chk('delete_never_a_handle', @() as_statement(@() delete(12345.25)));
g = figure(7); set(g, 'Visible', 'off');
close(g);
% A figure's handle is its number here (ADR 0051), so the number of a closed figure is a deleted
% handle and not a bare number.
u9b_chkdiv('delete_closed_number', @() as_statement(@() delete(7)), '0218');
u9b_chk('close_closed_number', @() as_statement(@() close(7)));
h = figure('Visible', 'off');
plot(1:3);
u9b_chk('printdlg_headless', @() as_statement(@() printdlg(h)));
u9b_chk('printpreview_headless', @() as_statement(@() printpreview(h)));
u9b_chk('exportsetupdlg_headless', @() as_statement(@() exportsetupdlg(h)));
close all force;

function r = as_statement(fn)
% 'ran', or the refusal's identifier and sentence.
try
    fn();
    r = 'ran';
catch err
    r = [err.identifier ' | ' err.message];
end
end
