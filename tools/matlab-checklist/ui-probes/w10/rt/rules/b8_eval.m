function b8_eval(s)
eval('a')
eval(['x=' num2str(1)])
eval(['disp(' s ')'])
eval('y = 2')
eval(s, 'disp(0)')
evalin('base', 'x')
evalc('disp(1)');
r = eval('1+1');
disp(r)
feval(@(x) x, 1);
feval(s, 1);
end
