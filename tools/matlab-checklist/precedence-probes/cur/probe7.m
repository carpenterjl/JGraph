cd(fileparts(mfilename('fullpath')));
if ~isfolder(fullfile('..','lib2')), mkdir(fullfile('..','lib2')); end
fid = fopen('plus.m','w'); fprintf(fid, 'function y = plus(varargin)\ny = -444;\nend\n'); fclose(fid);
fid = fopen('isequal.m','w'); fprintf(fid, 'function y = isequal(varargin)\ny = -445;\nend\n'); fclose(fid);
fid = fopen('cat.m','w'); fprintf(fid, 'function y = cat(varargin)\ny = -446;\nend\n'); fclose(fid);
fid = fopen('height.m','w'); fprintf(fid, 'function y = height(varargin)\ny = -222;\nend\n'); fclose(fid);
fid = fopen('minus.m','w'); fprintf(fid, 'function y = minus(varargin)\ny = -447;\nend\n'); fclose(fid);
fid = fopen('keys.m','w'); fprintf(fid, 'function y = keys(varargin)\ny = -448;\nend\n'); fclose(fid);
fid = fopen(fullfile('..','lib2','maker.m'),'w'); fprintf(fid, 'function f = maker()\nf = @(x) helper(x);\nend\nfunction y = helper(x)\ny = x * 100;\nend\n'); fclose(fid);
rehash;
addpath(fullfile('..','lib2'));
function show(label, f)
    try
        r = f();
        if isnumeric(r) || islogical(r), r = mat2str(r); elseif ischar(r), r = ['''' r '''']; else, r = class(r); end
        disp([label ' = ' r]);
    catch e
        disp([label ' err: ' e.message]);
    end
end
show('A plus(1,{1},"x") high in 2 and 3', @() plus(1, {1}, "x"));
show('B plus(1,2,"x") high in 3', @() plus(1, 2, "x"));
show('C isequal(1,2,"x") high in 3', @() isequal(1, 2, "x"));
show('D isequal(1,"x",{1})', @() isequal(1, "x", {1}));
show('E isequal(1,{1},"x")', @() isequal(1, {1}, "x"));
show('F cat(1,"x",{1})', @() cat(1, "x", {1}));
show('G height(table(1))', @() height(table(1)));
show('H height(1,table(1))', @() height(1, table(1)));
show('I minus(datetime(2020,1,2),datetime(2020,1,1))', @() minus(datetime(2020,1,2), datetime(2020,1,1)));
show('J minus(1,datetime(2020,1,1))', @() minus(1, datetime(2020,1,1)));
show('K minus(datetime(2020,1,1),1)', @() minus(datetime(2020,1,1), 1));
m = containers.Map({'a'},{1});
show('L keys(m)', @() keys(m));
show('M keys(1,m)', @() keys(1, m));
show('N minus(1,m)', @() minus(1, m));
show('O plus(1,m)', @() plus(1, m));
f = maker();
cd(fullfile('..','lib'));
show('P anon from path file after cd', @() f(2));
cd(fullfile('..','cur'));
show('Q plus(1,2)', @() plus(1, 2));
delete('plus.m'); delete('isequal.m'); delete('cat.m'); delete('height.m'); delete('minus.m'); delete('keys.m');
delete(fullfile('..','lib2','maker.m'));
