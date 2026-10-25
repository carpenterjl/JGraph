function v = k8make(kind)
%K8MAKE Return a value of the kind asked for, for the client to read back.
switch kind
    case 'double', v = 2.5;
    case 'row', v = [1 2 3];
    case 'matrix', v = [1 2 3; 4 5 6];
    case 'nd', v = reshape(1:8, 2, 2, 2);
    case 'empty', v = [];
    case 'empty03', v = zeros(0, 3);
    case 'single', v = single([1.5 2.5]);
    case 'int8', v = int8([-5 0 127]);
    case 'uint8', v = uint8([1 2; 3 4]);
    case 'int16', v = int16(-7);
    case 'uint16', v = uint16(7);
    case 'int32', v = int32([1 2 3]);
    case 'uint32', v = uint32(4000000000);
    case 'int64', v = int64(-9);
    case 'uint64', v = uint64(9);
    case 'complex', v = [1+2i, 3-4i];
    case 'complexscalar', v = 1-1i;
    case 'logical', v = [true false true];
    case 'logicalscalar', v = true;
    case 'char', v = 'hello';
    case 'charmatrix', v = ['abc'; 'def'];
    case 'emptychar', v = '';
    case 'cell', v = {1, 'two', [3 4; 5 6]};
    case 'nestedcell', v = {{1}, {}};
    case 'struct', v = struct('name', 'probe', 'value', [1 2 3]);
    case 'structarray', v = struct('a', {1, 2, 3});
    case 'emptystruct', v = struct([]);
    case 'string', v = "alpha";
    case 'strings', v = ["alpha", "beta"];
    case 'stringmissing', v = [string(missing), "x"];
    case 'sparse', v = sparse([1 3], [1 2], [5 6], 3, 3);
    case 'sparselogical', v = sparse([true false; false true]);
    case 'inf', v = [Inf -Inf NaN];
    case 'pi', v = pi;
    case 'large', v = 123456.789;
    case 'small', v = [1e-5 1];
    case 'mixed', v = [1 2.5 1000];
    case 'handle', v = @sin;
    otherwise, error('k8:kind', 'No kind %s.', kind);
end
end
