% JGraph's default installer logo for compiler.package.installer (stage K18), the size R2025b's
% packager resizes a logo to (290 by 112): the mark from default_splash.png on the splash's own wash,
% with the product's name as a band of darker blue. Run with jgraph -batch or matlab -batch beside
% default_splash.png; the image is checked in beside this file.
here = fileparts(mfilename('fullpath'));
splash = double(imread(fullfile(here, 'default_splash.png'))) / 255;
mark = imresize(splash(60:300, 80:320, :), [96 96]);
[~, y] = meshgrid(1:290, 1:112);
t = (y - 1) / 111;
top = [0.96 0.97 0.99];
bottom = [0.86 0.90 0.96];
canvas = zeros(112, 290, 3);
for c = 1:3
    canvas(:, :, c) = top(c) + (bottom(c) - top(c)) * t;
end
canvas(9:104, 13:108, :) = mark;
% A band of the mark's blue to the right of it, where an installer shows the product's name.
band = [0.22 0.38 0.62];
for c = 1:3
    canvas(44:68, 124:276, c) = band(c);
end
canvas = min(max(canvas, 0), 1);
imwrite(canvas, fullfile(here, 'default_logo.png'));
disp('made');
