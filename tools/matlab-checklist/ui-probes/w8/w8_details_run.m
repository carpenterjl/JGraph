here = fileparts(mfilename('fullpath'));
image = fullfile(tempdir, 'w8_probe_image.png');
imwrite(uint8(repmat(reshape([200 40 40], 1, 1, 3), 40, 60)), image);
w8_details(fullfile(here, 'w8_details.txt'), fullfile(here, '..', 'w0', 'rt', 'share', 'mltbx', 'fsroot', 'AcceptForm.mlapp'), image);
disp('done');
