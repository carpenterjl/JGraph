here = fileparts(mfilename('fullpath'));
root = getenv('W8_WORK');
try
    w8_read_back(fullfile(here, 'w8_read_back.txt'), fullfile(root, 'W8aBin.mlapp'), fullfile(root, 'W8aPlain.m'));
    disp('done');
catch e
    disp(getReport(e));
end
