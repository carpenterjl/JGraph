try; disp(['A max([1 5 3]) = ' num2str(max([1 5 3]))]); catch e; disp(['A err: ' e.message]); end
try; disp(['B mean([1 2 3]) = ' num2str(mean([1 2 3]))]); catch e; disp(['B err: ' e.message]); end
try; disp(['C which max: ' which('max')]); catch e; disp(['C err: ' e.message]); end
try; disp(['D which mean: ' which('mean')]); catch e; disp(['D err: ' e.message]); end
try; disp(['E exist max = ' num2str(exist('max')) ' exist mean = ' num2str(exist('mean'))]); catch e; disp(['E err: ' e.message]); end
try; disp(['F builtin max = ' num2str(builtin('max',[1 5 3]))]); catch e; disp(['F err: ' e.message]); end
try; addpath('lib'); disp(['G helper = ' helper()]); catch e; disp(['G err: ' e.message]); end
try; disp(['H localshadow = ' num2str(localshadow())]); catch e; disp(['H err: ' e.message]); end
try; disp(['I numel local = ' num2str(numel([1 2 3]))]); catch e; disp(['I err: ' e.message]); end
try; ascript; disp(['J ascript zz = ' num2str(exist('zz','var'))]); catch e; disp(['J err: ' e.message]); end
try; max = 7; disp(['K var max = ' num2str(max)]); clear max; disp(['L after clear max = ' num2str(max([1 5 3]))]); catch e; disp(['K/L err: ' e.message]); end
try; disp(['M feval mean = ' num2str(feval('mean',[1 2 3]))]); catch e; disp(['M err: ' e.message]); end
try; f = @mean; disp(['N handle mean = ' num2str(f([1 2 3]))]); catch e; disp(['N err: ' e.message]); end
try; disp(['O sum = ' num2str(sum([1 2 3]))]); catch e; disp(['O err: ' e.message]); end
function n = numel(x)
n = -444;
end
