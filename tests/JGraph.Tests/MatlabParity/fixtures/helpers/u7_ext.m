function s = u7_ext(file)
% A file's name and extension without its folder, which is the machine's (U7 fixtures).
[~, name, ext] = fileparts(file);
s = [name ext];
end
