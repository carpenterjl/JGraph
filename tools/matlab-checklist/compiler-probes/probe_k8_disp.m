function probe_k8_disp()
%PROBE_K8_DISP What R2025b's disp prints for numbers, logicals, text and sparse matrices under format
%short (compiler and SDK plan, stage K8): the display an MWArray's ToString answers, which JGraph's
%MWArray API draws itself. Each value prints as a <<key>> line, then disp's output with every line
%between | marks, so blanks and leading spaces survive. Run under matlab -batch -noFigureWindows.
format short
vals = {
    'int1', 1; 'int2', 22; 'int3', 333; 'int4', 4444; 'int5', 55555; 'int6', 666666; 'int9', 123456789; 'int10', 1234567890;
    'row1', [1 2 3]; 'row3', [1 22 333]; 'row4', [1 22 333 4444]; 'row5', [1 55555]; 'row6', [1 666666]; 'row9', [1 123456789];
    'row10', [1 1234567890]; 'rowneg', [-1 2]; 'rowneg3', [-100 2]; 'rowneg4', [-1000 2]; 'col', [1; 2; 3]; 'mat', [1 2 3; 4 5 6];
    'matneg', [-1 20; 300 -4]; 'zero', 0; 'zeros', [0 0]; 'negzero', -0;
    'frac', 2.5; 'pi', pi; 'negpi', -pi; 'small', 0.001; 'smaller', 0.0001; 'tiny', 1e-10; 'big', 99999.5; 'bigger', 123456.789;
    'huge', 1e20; 'fracrow', [1.5 2]; 'fracneg', [-1.5 2; 3 4]; 'fracrow100', [1 2.5 100]; 'fracrow999', [1 2.5 999];
    'scale3', [1 2.5 1000]; 'scale4', [1 2.5 12345]; 'scale5', [1.5 123456]; 'scalesmall', [0.001 0.0025]; 'scalesmaller', [0.0001 0.00025];
    'mixsmall', [1e-5 1]; 'mixbig', [1e5 1.5]; 'mixhuge', [1e10 1.5]; 'fraccol', [0.5; 1.25]; 'fracmat', [1.5 2.25; 3 4];
    'inf', Inf; 'nan', NaN; 'neginf', -Inf; 'infrow', [Inf -Inf NaN]; 'infint', [1 Inf]; 'inffrac', [1.5 Inf NaN]; 'nanint', [NaN 2 3];
    'infscale', [Inf 1 2.5 1000];
    'cplx', 1+2i; 'cplxneg', 3-4i; 'cplxrow', [1+2i, 3-4i]; 'cplxmat', [1+5i 2+6i; 3+7i 4+8i]; 'cplxint', complex(3, 4); 'cplxzero', complex(0, 0);
    'cplxfrac', 1.5+0.25i; 'cplxbig', 1000+1i; 'cplxrowbig', [1+1i 1000+2i]; 'cplxneg2', -1-2i; 'cplxinf', complex(Inf, 1);
    'single', single(2.5); 'singlerow', single([1.5 2]); 'singlemat', single([1.5 2; 3 4]); 'singleint', single([1 2 3]);
    'int8', int8([-5 0 127]); 'uint8', uint8([1 2; 3 4]); 'uint8big', uint8([1 20; 200 4]); 'int16', int16(-7); 'int16row', int16([-1 2]);
    'int32', int32([1 2 3]); 'int32mat', int32([1 20; 300 4]); 'int64', int64(-9); 'int64big', int64(123456789012); 'uint64', uint64([1 2]);
    'int32cplx', complex(int32(3), int32(4)); 'int8cplxrow', complex(int8([1 2]), int8([3 -4]));
    'logical', true; 'logicalrow', [true false true]; 'logicalmat', [true false; false false];
    'char', 'hello'; 'charmat', ['abc'; 'def']; 'charspace', ' x '; 'charpad', ['a  '; 'bcd'];
    'nd', reshape(1:8, 2, 2, 2); 'ndfrac', reshape([1.5 2 3 4 5 6 7 8], 2, 2, 2); 'nd4', reshape(1:16, 2, 2, 2, 2); 'ndlogical', true(1, 1, 2);
    'longrow', 1:30; 'longfrac', (1:20) / 3; 'longmat', reshape(1:40, 2, 20); 'longcplx', (1:8) + 1i; 'longint8', int8(1:40);
    'longlogical', true(1, 45); 'widerow', [1 22 333 4444 55555 1 2 3 4 5];
    'sparse', sparse([1 3], [1 2], [5 6], 3, 3); 'sparsefrac', sparse([1 2], [1 2], [1.5 2]); 'sparsebig', sparse(10, 12, 7);
    'sparsecplx', sparse(1, 2, 5+1i); 'sparsezero', sparse(2, 2); 'sparselogical', sparse([true false; false true]);
    'sparsescale', sparse([1 2], [1 1], [1.5 1000], 2, 1); 'sparseneg', sparse([1 2], [1 2], [-1 2]);
    };
for k = 1:size(vals, 1)
    fprintf('<<%s>>\n', vals{k, 1});
    txt = evalc('disp(vals{k, 2})');
    lines = splitlines(txt);
    if ~isempty(lines) && isempty(lines{end}), lines(end) = []; end
    for j = 1:numel(lines)
        fprintf('|%s|\n', lines{j});
    end
end
fprintf('<<end>>\n');
end
