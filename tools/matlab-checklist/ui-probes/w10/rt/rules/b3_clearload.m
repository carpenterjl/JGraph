function b3_clearload()
clear
clear all
clear variables
clear x
clear global
clear functions
clear('y')
load a.mat
load('a.mat')
load a.mat x
load('a.mat', 'x')
d = load('a.mat');
disp(d)
end
