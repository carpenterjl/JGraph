% make-splash.m — the animation the startup splash is drawn on.
%
% This is SurfaceLerpTest_5 — the same five shapes, the same five sweeps, the same lit band
% travelling across the sheet and rewriting it as it passes — rendered at the splash window's own
% size and written to splash.apng beside this file. The application ships that file, copies it next
% to the executable and plays it behind the wordmark while the container warms up (SplashWindow).
%
% Nothing about the picture is re-invented here. What differs from the demo script is the frame
% size and the frame rate, both chosen so the asset is something a program can read at startup:
% 560 by 368 at 24 frames a second is about eight megabytes, where the demo's 700 by 460 at 30 is
% eighteen. The tour still runs its full five legs and still closes back onto its first frame, so
% the splash can loop it for as long as loading takes without a seam.
%
% Three things make it possible and none of them is a workaround:
%   figure('Color', 'none')       the page is not painted, so the splash is the shape of the
%                                 surface rather than a rectangle laid over the desktop,
%   axis off                      the axes' furniture goes and its children stay,
%   VideoWriter(f, 'Animated PNG')  the only container here that carries an alpha channel — not
%                                 one of MATLAB's seven profiles has one to carry (ADR 0114).

% ---- Timing -------------------------------------------------------------
fps       = 24;
legFrames = 36;                     % 5 legs -> 180 frames -> 7.5 s
grid_n    = 120;

% ---- Look ---------------------------------------------------------------
frontW   = 0.20;                    % width of the wavefront, in sweep units
glowGain = 0.48;                    % how hard the front burns into the white tip
outFile  = 'splash.apng';

[X, Y] = meshgrid(linspace(-3, 3, grid_n), linspace(-3, 3, grid_n));
R  = sqrt(X.^2 + Y.^2) + eps;
Th = atan2(Y, X);

% ---- The five shapes ----------------------------------------------------
shapes = { ...
    cos(2.2*R) .* exp(-R.^2/9), ...                      % rings
    sin(1.7*X + 0.8*sin(1.3*Y)) .* ...
        (0.5 + 0.5*cos(0.95*Y)), ...                     % dunes
    cos(1.9*X) .* cos(1.9*Y), ...                        % lattice
    sin(2.5*R - 3*Th) .* tanh(1.2*R) .* exp(-R/4), ...   % vortex
    exp(-((X-1.1).^2 + (Y+1.1).^2)/1.4) + ...
        exp(-((X+1.1).^2 + (Y-1.1).^2)/1.4) - ...
        0.55*exp(-R.^2/6) };                             % paired mounds

for i = 1:numel(shapes)
    shapes{i} = shapes{i} / max(abs(shapes{i}(:)));
end

% ---- The five sweeps ----------------------------------------------------
% Each is a scalar field over the grid, scaled so 0 is where the front starts and
% 1 is where it finishes. The front is then a single number per frame.
sweeps = { ...
    (X + 3.2) / 6.4, ...            % left to right
    R / 4.4, ...                    % out from the middle
    ((X + Y)/2 + 3.2) / 6.4, ...    % along the diagonal
    1 - R / 4.4, ...                % back in to the middle
    (Y + 3.2) / 6.4 };              % front to back

% Base palettes, one per leg; the white tip is added to whichever is in force.
bases = { turbo(200), cool(200), hot(200), winter(200), spring(200) };

nLegs     = numel(shapes);
numFrames = nLegs * legFrames;

% ---- Figure -------------------------------------------------------------
fig = figure('Color', 'none', 'Position', [100 100 560 368]);
hSurf = surf(X, Y, shapes{1}, 'EdgeColor', 'none');
shading interp
% SurfaceLerpTest_2 asked for half again as much room as the data needs, and set
% the axes taller than the figure to bleed the frame it could not turn off. Both
% were paid to a camva that did nothing and an `axis off` that was thrown away.
% Neither is needed now: the limits are the data's, the axes fills the figure,
% and the angle below is the one that frames it.
axis([-3.3 3.3 -3.3 3.3 -1.3 1.3]);
axis vis3d
axis off                            % the axes' furniture goes; the surface stays
clim([0 1]);
colormap(withWhiteTip(bases{1}));
light('Position', [-1.5 -1 2.5], 'Style', 'infinite');
lighting gouraud
material dull
set(gca, 'Position', [0 0 1 1]);

vid = VideoWriter(outFile, 'Animated PNG');
vid.FrameRate = fps;
open(vid);

for k = 1:numFrames
    u   = (k - 1) / legFrames;
    leg = floor(u);
    s   = u - leg;

    A   = shapes{leg + 1};
    B   = shapes{mod(leg + 1, nLegs) + 1};
    phi = sweeps{leg + 1};

    % The front crosses the whole field with a little runway at each end, so it is
    % fully off the sheet when the leg changes hands and nothing pops.
    f = -frontW + (1 + 2*frontW) * smoothstep(s);

    % Behind the front the new shape, ahead of it the old one, and one front-width
    % of blend in between.
    w = smoothstep(clamp01((f - phi) / frontW + 0.5));
    Z = (1 - w) .* A + w .* B;

    % Colour is height for the body of the sheet and a bright ridge at the front,
    % which is what makes the transition legible from any camera angle — and it is
    % the only thing marking it once there is no box to see the sheet against.
    glow = exp(-((phi - f) / (0.40*frontW)).^2);
    C    = clamp01(0.72 * (Z + 1) / 2 + glowGain * glow);

    set(hSurf, 'ZData', Z, 'CData', C);
    colormap(withWhiteTip(mixMaps(bases, leg, s, nLegs)));

    tAll = (k - 1) / numFrames;
    view(42 + 360*tAll, 30 + 11*sin(2*pi*tAll));
    % Chosen against the whole tour, not one pose: the sheet is widest side-on and
    % tallest at the top of the elevation swing, and 5.8 clears both with room over.
    camva(5.8);

    drawnow limitrate

    % A capture of a figure with no page is four channels rather than three, and
    % the fourth is the shape of the cut-out. It goes into the file as it stands.
    writeVideo(vid, getframe(fig));
end

close(vid);

d = dir(outFile);
fprintf('wrote %s — %d transparent frames, %.1f s at %d fps, %.2f MB\n', ...
        outFile, numFrames, numFrames / fps, fps, d(1).bytes / 1048576);

% ========================================================================
function y = smoothstep(t)
    y = t .* t .* (3 - 2*t);
end

function y = clamp01(x)
    y = min(1, max(0, x));
end

function cm = withWhiteTip(base)
    % The top fifth of the table runs from the palette's own brightest colour up to
    % white. Nothing on the surface reaches it except the wavefront, so the front
    % reads as light rather than as another colour.
    n   = size(base, 1);
    tip = linspace(0, 1, 56)';
    top = (1 - tip) * base(n, :) + tip * [1 1 1];
    cm  = [base; top];
end

function cm = mixMaps(bases, leg, s, nLegs)
    % Held for most of the leg, then handed over quickly — a long linear crossfade
    % between two colour tables spends its middle in the grey where they average.
    t  = smoothstep(min(1, max(0, (s - 0.6) / 0.35)));
    ca = bases{mod(leg, nLegs) + 1};
    cb = bases{mod(leg + 1, nLegs) + 1};
    cm = (1 - t) * ca + t * cb;
end
