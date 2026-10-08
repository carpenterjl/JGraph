function out = u10_mode(set)
% Which method U10Bad fails in: u10_mode('setup') says so, u10_mode() answers it ('none' at first).
persistent MODE
if isempty(MODE), MODE = 'none'; end
if nargin == 1
    MODE = set;
end
out = MODE;
end
