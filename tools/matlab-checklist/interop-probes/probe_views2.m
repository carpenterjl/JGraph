% probe_views2: what methods says of a library, a lib.pointer and a libstruct (interop plan,
% stage 10), which methodsview and libfunctionsview are built on: the printed listings, the cell
% answers, and each -full line. No window opens.
a = ip_assets();
addpath(a.here); addpath(a.root);
lib = 'jgtestlib';
loadlibrary(a.lib, @jgtestlib_proto);
lp = libpointer('doublePtr', [1 2 3]);
ls = libstruct('jg_point');

ip_px('print.lib', 'methods(''lib.jgtestlib'')');
ip_px('print.lib.full', 'methods(''lib.jgtestlib'', ''-full'')');
ip_px('print.pointer', 'methods(lp)');
ip_px('print.pointer.name', 'methods(''lib.pointer'')');
ip_px('print.pointer.full', 'methods(lp, ''-full'')');
ip_px('print.struct', 'methods(ls)');
ip_px('print.struct.name', 'methods(''lib.jg_point'')');
ip_px('print.struct.full', 'methods(ls, ''-full'')');
ip_px('print.lib.unloaded', 'methods(''lib.nolib'')');
ip_px('print.lib.bare', 'methods lib.jgtestlib');

names = {'lib.jgtestlib', 'lib.pointer', 'lib.jg_point'};
for k = 1:numel(names)
    for full = [false true]
        try
            if full
                m = methods(names{k}, '-full');
                tag = 'full';
            else
                m = methods(names{k});
                tag = 'names';
            end
            fprintf('cell.%s.%s.size\t%d %d\n', names{k}, tag, size(m, 1), size(m, 2));
            for r = 1:numel(m)
                fprintf('cell.%s.%s.%03d\t%s\n', names{k}, tag, r, m{r});
            end
        catch e
            fprintf('cell.%s.%s\tERR %s %s\n', names{k}, tag, e.identifier, e.message);
        end
    end
end

ip_pr('cell.obj.pointer.full.n', 'numel(methods(lp, ''-full''))');
ip_pr('cell.obj.struct.full.n', 'numel(methods(ls, ''-full''))');
ip_pr('ismethod.isNull', 'ismethod(lp, ''isNull'')');
ip_pr('ismethod.structsize', 'ismethod(ls, ''structsize'')');

clear lp ls
unloadlibrary(lib);
