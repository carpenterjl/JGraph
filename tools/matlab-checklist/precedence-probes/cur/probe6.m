cd(fileparts(mfilename('fullpath')));
function show(label, f)
    try
        r = f();
        if isnumeric(r) || islogical(r), r = mat2str(r); elseif ischar(r), r = ['''' r '''']; elseif iscell(r), r = strjoin(cellfun(@(c) char(c), r, 'UniformOutput', false), ' | '); else, r = class(r); end
        disp([label ' = ' r]);
    catch e
        disp([label ' err: ' e.message]);
    end
end
w = which('numel','-all'); disp('W0 which -all numel (unshadowed):'); disp(strjoin(w, ' | '));
w = which('size','-all'); disp('W1 which -all size (unshadowed):'); disp(strjoin(w(1:min(4,end)), ' | '));
w = which('length','-all'); disp('W2 which -all length (unshadowed):'); disp(strjoin(w(1:min(4,end)), ' | '));
fid = fopen('numel.m','w'); fprintf(fid, 'function y = numel(varargin)\ny = -111;\nend\n'); fclose(fid);
fid = fopen('length.m','w'); fprintf(fid, 'function y = length(varargin)\ny = -333;\nend\n'); fclose(fid);
fid = fopen('plus.m','w'); fprintf(fid, 'function y = plus(varargin)\ny = -444;\nend\n'); fclose(fid);
rehash;
show('A numel("x",1)', @() numel("x", 1));
show('B numel(1,"x")', @() numel(1, "x"));
show('C numel("x",{1})', @() numel("x", {1}));
show('D numel({1},"x")', @() numel({1}, "x"));
show('E numel(int8(1),"x")', @() numel(int8(1), "x"));
show('F numel("x","y")', @() numel("x", "y"));
show('G numel(1,2)', @() numel(1, 2));
show('H numel(1,''x'')', @() numel(1, 'x'));
show('I length("x")', @() length("x"));
show('J length(1,"x")', @() length(1, "x"));
show('K length("x",1)', @() length("x", 1));
show('L plus(1,2)', @() plus(1, 2));
show('M plus(1,"x")', @() plus(1, "x"));
show('N plus("x",1)', @() plus("x", 1));
show('O 1 + "x" operator', @() 1 + "x");
show('P plus({1},2)', @() plus({1}, 2));
show('Q plus(1,{2})', @() plus(1, {2}));
show('R plus(1,int8(2))', @() plus(1, int8(2)));
show('S plus(1,true)', @() plus(1, true));
show('T plus(1,''x'')', @() plus(1, 'x'));
show('U plus(''x'',1)', @() plus('x', 1));
show('V plus(1,datetime(2020,1,1))', @() plus(1, datetime(2020,1,1)));
show('W plus(datetime(2020,1,1),1)', @() plus(datetime(2020,1,1), 1));
show('X plus(1,struct(''a'',1))', @() plus(1, struct('a',1)));
show('Y plus(1,@sin)', @() plus(1, @sin));
delete('numel.m'); delete('length.m'); delete('plus.m');
