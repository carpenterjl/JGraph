% U7b: the same char row saved in the default (version 7) format, whose elements are compressed.
s = char([65 233 20013 8364 66]); %#ok<NASGU>
save(fullfile('rt', 'char_v7.mat'), 's');
q = load(fullfile('rt', 'char_v7.mat'));
fprintf('round trip: %s\n', mat2str(double(q.s)));
