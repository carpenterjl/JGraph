function g = acc_greet(name)
%ACC_GREET The acceptance set's text answer: a greeting from a name (stage K22).
arguments
    name (1,:) char
end
g = ['Hello, ' name '!'];
end
