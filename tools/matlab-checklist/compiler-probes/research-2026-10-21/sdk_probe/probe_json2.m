cd(fileparts(mfilename('fullpath')));
t = {
 'sparse 3x4 (2,3)=7', sparse(2,3,7,3,4);
 'sparse 3x4 two', sparse([1 3],[2 4],[5 6],3,4);
 'sparse logical', sparse(logical([0 1;1 0]));
 'sparse complex', sparse([1+2i 0;0 3]);
 'sparse empty', sparse(2,2);
 'value object', PtVal;
 'value object array', [PtVal PtVal];
 'duration', seconds(90);
 'datetime row', datetime(2020,1,1:2);
 'NaT', NaT;
 'datetime tz', datetime(2020,1,1,'TimeZone','UTC');
 'enum row', [matlab.lang.OnOffSwitchState.on matlab.lang.OnOffSwitchState.off];
 'char 2x2x2', reshape('abcdefgh',2,2,2);
 'struct with matrix', struct('m',[1 2;3 4]);
 'cell with struct', {struct('a',1)};
 'struct array nested', struct('a',{struct('b',1), struct('b',2)});
 'empty string ""', "";
 'int8 scalar', int8(5);
 'single scalar', single(2.5);
 'double 1e-300', 1e-300;
 'double pi', pi;
 'double 0.1', 0.1;
 'double 1e21', 1e21;
 'double 123456789', 123456789;
 'logical 0x0', logical([]);
 'uint8 emptyrow', uint8(zeros(1,0));
 'char col', ['a';'b'];
 'handle object', containers.Map;
 'string 2x1', ["a";"b"];
 'unicode', char([945 946 8364]);
 'complex single', single(1+2i);
 'complex int16', int16(3+4i);
 'cell of empty', {[]};
};
for k = 1:size(t,1)
  for fm = {'small','large'}
    try
      j = mps.json.encode(t{k,2},'Format',fm{1});
      fprintf('ENC [%s] %-22s : %s\n', fm{1}, t{k,1}, j);
      try, d = mps.json.decode(j); fprintf('    DEC class=%s size=%s isequal=%d\n', class(d), mat2str(size(d)), isequaln(d,t{k,2})); catch e2, fprintf('    DEC ERR %s\n', e2.message); end
    catch e
      fprintf('ENC [%s] %-22s : ERR %s : %s\n', fm{1}, t{k,1}, e.identifier, e.message);
    end
  end
end
j = mps.json.encode(char([945 946 8364])); fprintf('unicode bytes: %s\n', mat2str(double(j)));
js = {'{"mwdata":[[1],[2],[7]],"mwsize":[3,4],"mwsparse":true,"mwtype":"double"}', ...
      '{"mwdata":{"X":[1],"Y":["a"]},"mwsize":[1,1],"mwtype":"PtVal"}', ...
      '{"mwdata":[{"mwdata":"NaN"}],"mwsize":[1,1],"mwtype":"single"}', ...
      '{"mwdata":[1,2,3],"mwsize":[1,3],"mwtype":"double","extra":5}', ...
      '{"mwdata":[1],"mwsize":[1,1]}', ...
      '{"mwtype":"double","mwsize":[2,1],"mwdata":[1,2]}', ...
      '{"mwdata":["on"],"mwsize":[1,1],"mwtype":"matlab.lang.OnOffSwitchState"}', ...
      '{"mwdata":[300],"mwsize":[1,1],"mwtype":"uint8"}', ...
      '{"mwdata":[1.7],"mwsize":[1,1],"mwtype":"int32"}', ...
      '{"mwdata":["\u03b1"],"mwsize":[1,1],"mwtype":"char"}', ...
      '{"a":[1,2],"b":{"c":true}}', '[[1,2],[3]]', '[[true,1]]', '{"mwdata":[],"mwsize":[0,0],"mwtype":"struct"}'};
for k = 1:numel(js)
  try
    d = mps.json.decode(js{k});
    fprintf('DEC %-80s -> class=%s size=%s val=%s\n', js{k}, class(d), mat2str(size(d)), strtrim(evalc('disp(d)')));
  catch e
    fprintf('DEC %-80s -> ERR %s : %s\n', js{k}, e.identifier, e.message);
  end
end
