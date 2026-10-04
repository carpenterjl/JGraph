% EX3_UIFIGURE_GRID  Programmatic uifigure app written as a plain script:
% uifigure + uigridlayout + uiaxes + modern components, anonymous-function
% callbacks that close over handles created earlier, local functions at the end.
% Pattern of R2019b+ course material ("build an app without App Designer").

fig = uifigure('Name', 'Filter designer', 'Position', [100 100 760 460]);
gl = uigridlayout(fig, [4 3]);
gl.RowHeight = {'1x', 'fit', 'fit', 30};
gl.ColumnWidth = {180, '1x', '1x'};

ax = uiaxes(gl);
ax.Layout.Row = 1;
ax.Layout.Column = [2 3];
title(ax, 'Magnitude response');
xlabel(ax, 'Normalized frequency');
ylabel(ax, '|H| (dB)');
grid(ax, 'on');

pnl = uipanel(gl, 'Title', 'Design');
pnl.Layout.Row = [1 3];
pnl.Layout.Column = 1;
pg = uigridlayout(pnl, [6 1]);
pg.RowHeight = {'fit', 'fit', 'fit', 'fit', 'fit', '1x'};

uilabel(pg, 'Text', 'Type');
typeDD = uidropdown(pg, 'Items', {'lowpass', 'highpass'}, 'Value', 'lowpass');
uilabel(pg, 'Text', 'Order');
orderSpin = uispinner(pg, 'Limits', [1 12], 'Value', 4, 'RoundFractionalValues', 'on');
logCB = uicheckbox(pg, 'Text', 'dB scale', 'Value', true);

cutLabel = uilabel(gl, 'Text', 'Cutoff: 0.30');
cutLabel.Layout.Row = 2;
cutLabel.Layout.Column = 2;
cutSlider = uislider(gl, 'Limits', [0.01 0.99], 'Value', 0.3);
cutSlider.Layout.Row = 3;
cutSlider.Layout.Column = [2 3];

statusLbl = uilabel(gl, 'Text', 'Ready', 'FontColor', [0.3 0.3 0.3]);
statusLbl.Layout.Row = 4;
statusLbl.Layout.Column = [1 2];
exportBtn = uibutton(gl, 'push', 'Text', 'Export coefficients');
exportBtn.Layout.Row = 4;
exportBtn.Layout.Column = 3;

% Anonymous callbacks: every one calls the same redraw with the handles it needs.
redrawFcn = @(~, ~) redraw(ax, typeDD, orderSpin, cutSlider, logCB, statusLbl);
typeDD.ValueChangedFcn = redrawFcn;
orderSpin.ValueChangedFcn = redrawFcn;
logCB.ValueChangedFcn = redrawFcn;
cutSlider.ValueChangedFcn = redrawFcn;
cutSlider.ValueChangingFcn = @(~, evt) set(cutLabel, 'Text', sprintf('Cutoff: %.2f', evt.Value));
exportBtn.ButtonPushedFcn = @(src, ~) exportCoefficients(fig, typeDD, orderSpin, cutSlider);

redraw(ax, typeDD, orderSpin, cutSlider, logCB, statusLbl);

function redraw(ax, typeDD, orderSpin, cutSlider, logCB, statusLbl)
[b, a] = design(typeDD.Value, orderSpin.Value, cutSlider.Value);
w = linspace(0, pi, 512);
z = exp(1i*w);
H = polyval(b, z) ./ polyval(a, z);
mag = abs(H);
if logCB.Value
    mag = 20*log10(max(mag, 1e-6));
end
plot(ax, w/pi, mag, 'LineWidth', 1.25);
statusLbl.Text = sprintf('%s, order %d, cutoff %.2f', typeDD.Value, orderSpin.Value, cutSlider.Value);
end

function [b, a] = design(kind, n, wc)
% Butterworth by bilinear transform without the Signal Processing Toolbox.
wa = tan(pi*wc/2);
k = 1:n;
p = wa * exp(1i*pi*(2*k + n - 1)/(2*n));      % analog poles (lowpass)
if strcmp(kind, 'highpass')
    p = wa^2 ./ p;
    zd = ones(1, n);
else
    zd = -ones(1, n);
end
pd = (1 + p) ./ (1 - p);
b = real(poly(zd));
a = real(poly(pd));
g = abs(polyval(a, 1 - 2*strcmp(kind, 'highpass'))) / abs(polyval(b, 1 - 2*strcmp(kind, 'highpass')));
b = b * g;
end

function exportCoefficients(fig, typeDD, orderSpin, cutSlider)
[b, a] = design(typeDD.Value, orderSpin.Value, cutSlider.Value);
assignin('base', 'b', b);
assignin('base', 'a', a);
uialert(fig, 'Coefficients written to b and a in the base workspace.', 'Exported', 'Icon', 'success');
end
