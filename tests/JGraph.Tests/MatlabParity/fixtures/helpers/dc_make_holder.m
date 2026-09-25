function h = dc_make_holder(tag)
% Hands a DeleteHolder out to whoever called (V11): the caller's binding is its holder, and the
% destructor runs when that binding goes - a JGS rebinding included.
h = DeleteHolder(tag);
end
