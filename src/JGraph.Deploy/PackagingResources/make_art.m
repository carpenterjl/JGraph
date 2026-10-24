% JGraph's default icon and splash for compiler.build (stage K6), made from the application's icon:
% jgraph256.png is the 256-pixel PNG image inside src/JGraph.Application/Assets/jgraph.ico. Run with
% matlab -batch or jgraph -batch beside that PNG; the two images are checked in beside this file.
here = fileparts(mfilename('fullpath'));
[icon, ~, alpha] = imread(fullfile(here, 'jgraph256.png'));
icon = double(icon) / 255;
alpha = double(alpha) / 255;

small = double(imresize(icon, [48 48]));
smallAlpha = min(max(double(imresize(alpha, [48 48])), 0), 1);
imwrite(small, fullfile(here, 'default_icon_48.png'), 'Alpha', smallAlpha);

% The splash: the icon on a soft vertical wash, 400 by 400.
[~, y] = meshgrid(1:400, 1:400);
t = (y - 1) / 399;
top = [0.96 0.97 0.99];
bottom = [0.83 0.88 0.95];
canvas = zeros(400, 400, 3);
for c = 1:3
    canvas(:, :, c) = top(c) + (bottom(c) - top(c)) * t;
end
mark = double(imresize(icon, [220 220]));
markAlpha = min(max(double(imresize(alpha, [220 220])), 0), 1);
rows = 70:289;
cols = 91:310;
for c = 1:3
    canvas(rows, cols, c) = canvas(rows, cols, c) .* (1 - markAlpha) + mark(:, :, c) .* markAlpha;
end
canvas = min(max(canvas, 0), 1);
imwrite(canvas, fullfile(here, 'default_splash.png'));
disp('made');
