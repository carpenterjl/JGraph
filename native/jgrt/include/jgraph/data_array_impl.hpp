/*
 * jgraph/data_array_impl.hpp - the definitions jgraph/data_array.hpp's classes need once all of
 * them are declared, and the factory, the ordered iterators, the visitors and the element ranges
 * (compiler and SDK plan, stage K12). Included by data_array.hpp; not for inclusion on its own.
 */
#ifndef JGRAPH_DATA_ARRAY_IMPL_HPP
#define JGRAPH_DATA_ARRAY_IMPL_HPP

namespace jgraph {
namespace data {

// --- Array ---------------------------------------------------------------------------------------

// A default Array has no storage: empty, 0-by-0, of no type (local: empty.default).
inline ArrayType Array::getType() const { return pImpl ? pImpl->type : ArrayType::UNKNOWN; }
inline MemoryLayout Array::getMemoryLayout() const { return pImpl ? pImpl->layout : MemoryLayout::COLUMN_MAJOR; }
inline ArrayDimensions Array::getDimensions() const noexcept { return pImpl ? pImpl->dims : ArrayDimensions{0, 0}; }
inline size_t Array::getNumberOfElements() const noexcept { return pImpl ? getNumElements(pImpl->dims) : 0; }
inline bool Array::isEmpty() const noexcept { return getNumberOfElements() == 0; }

inline ArrayElementRef<false> Array::operator[](size_t idx) {
    detail::Access::writable(*this);
    return ArrayElementRef<false>(detail::NewRef(pImpl, idx));
}

inline ArrayElementRef<true> Array::operator[](size_t idx) const {
    if (!pImpl) {
        throw CantIndexIntoEmptyArrayException("Can't index into an empty array.");
    }
    return ArrayElementRef<true>(detail::NewRef(pImpl, idx));
}

// --- References to held arrays -------------------------------------------------------------------

inline ArrayType ArrayReferenceExt::getType() const { return detail::Slot(*pImpl).getType(); }
inline ArrayDimensions ArrayReferenceExt::getDimensions() const { return detail::Slot(*pImpl).getDimensions(); }
inline size_t ArrayReferenceExt::getNumberOfElements() const { return detail::Slot(*pImpl).getNumberOfElements(); }
inline bool ArrayReferenceExt::isEmpty() const { return detail::Slot(*pImpl).isEmpty(); }

template <bool is_const_ref>
inline ArrayElementRef<is_const_ref> IndexableArrayRef<is_const_ref>::operator[](size_t idx) {
    return ArrayElementRef<is_const_ref>(detail::NewRef(detail::SlotStorage(*pImpl, !is_const_ref), idx));
}

template <bool is_const_ref>
inline ArrayElementRef<true> IndexableArrayRef<is_const_ref>::operator[](size_t idx) const {
    return ArrayElementRef<true>(detail::NewRef(detail::SlotStorage(*pImpl, false), idx));
}

// --- Struct elements -----------------------------------------------------------------------------

namespace detail {

/// A struct element as R2025b's API takes one: its subscripts checked when a field is read, not
/// when the element is taken (k12 kind probe: s[5] taken, s[5]["a"] refused).
inline Struct Reader<Struct>::Read(const RefImpl& r) {
    if (r.owner->type != ArrayType::STRUCT || r.hasField) {
        ThrowMismatch();
    }
    return Struct(std::make_shared<RefImpl>(r));
}

/// Where a struct element's fields start among its array's elements.
inline size_t FirstField(const RefImpl& r) {
    return Place(r) * r.owner->fields.size();
}

/// The fields of an element past the end, iterated: none (R2025b's iterators end the process there).
inline size_t FieldCount(const RefImpl& r, size_t& first) noexcept {
    try {
        first = FirstField(r);
        return r.owner->fields.size();
    } catch (...) {
        first = 0;
        return 0;
    }
}

} // namespace detail

inline Array Struct::operator[](std::string idx) const {
    const impl::ArrayImpl& a = *pImpl->owner;
    return a.elements[detail::FirstField(*pImpl) + detail::FieldIndex(a, idx)];
}

inline Struct::const_iterator Struct::begin() const noexcept {
    size_t first;
    detail::FieldCount(*pImpl, first);
    return const_iterator(pImpl->owner, 0, first, true);
}
inline Struct::const_iterator Struct::cbegin() const noexcept { return begin(); }
inline Struct::const_iterator Struct::end() const noexcept {
    size_t first;
    size_t n = detail::FieldCount(*pImpl, first);
    return const_iterator(pImpl->owner, n, first, true);
}
inline Struct::const_iterator Struct::cend() const noexcept { return end(); }

inline Reference<Array> Reference<Struct>::operator[](std::string idx) {
    return detail::Access::createRef<Reference<Array>>(detail::Then(*pImpl, std::move(idx)));
}
inline Array Reference<Struct>::operator[](std::string idx) const { return Struct(pImpl)[std::move(idx)]; }
inline Reference<Struct>::iterator Reference<Struct>::begin() noexcept {
    size_t first;
    detail::FieldCount(*pImpl, first);
    return iterator(pImpl->owner, 0, first, true);
}
inline Reference<Struct>::iterator Reference<Struct>::end() noexcept {
    size_t first;
    size_t n = detail::FieldCount(*pImpl, first);
    return iterator(pImpl->owner, n, first, true);
}
inline Reference<Struct>::const_iterator Reference<Struct>::begin() const noexcept { return Struct(pImpl).begin(); }
inline Reference<Struct>::const_iterator Reference<Struct>::cbegin() const noexcept { return Struct(pImpl).begin(); }
inline Reference<Struct>::const_iterator Reference<Struct>::end() const noexcept { return Struct(pImpl).end(); }
inline Reference<Struct>::const_iterator Reference<Struct>::cend() const noexcept { return Struct(pImpl).end(); }
inline Reference<Struct>::operator Struct() const noexcept { return Struct(pImpl); }

// --- Filling arrays from values ------------------------------------------------------------------

namespace detail {

/// A value given to the factory, as an element of the array made from it.
template <typename E>
struct Element {
    template <typename V>
    static E From(const V& value) {
        return static_cast<E>(value);
    }
};

template <>
struct Element<MATLABString> {
    static MATLABString From(const MATLABString& value) { return value; }
    static MATLABString From(const String& value) { return MATLABString(value); }
    static MATLABString From(const std::string& value) { return MATLABString(FromAscii(value)); }
    static MATLABString From(const char* value) { return MATLABString(FromAscii(value)); }
    static MATLABString From(const char16_t* value) { return MATLABString(String(value)); }
};

template <>
struct Element<Array> {
    static Array From(const Array& value) { return value; }
};

/// The place in column-major storage of the k-th element in row-major order.
inline size_t ColumnPlaceOfRowOrder(const ArrayDimensions& dims, size_t k) {
    size_t place = 0;
    size_t stride = 1;
    std::vector<size_t> strides(dims.size());
    for (size_t d = 0; d < dims.size(); d++) {
        strides[d] = stride;
        stride *= dims[d];
    }
    for (size_t d = dims.size(); d-- > 0;) {
        size_t extent = dims[d] == 0 ? 1 : dims[d];
        place += (k % extent) * strides[d];
        k /= extent;
    }
    return place;
}

/// The place in row-major storage of the k-th element in column-major order.
inline size_t RowPlaceOfColumnOrder(const ArrayDimensions& dims, size_t k) {
    size_t place = 0;
    std::vector<size_t> strides(dims.size());
    size_t stride = 1;
    for (size_t d = dims.size(); d-- > 0;) {
        strides[d] = stride;
        stride *= dims[d];
    }
    for (size_t d = 0; d < dims.size(); d++) {
        size_t extent = dims[d] == 0 ? 1 : dims[d];
        place += (k % extent) * strides[d];
        k /= extent;
    }
    return place;
}

template <typename E, bool Dense = IsDense<E>::value>
struct Store {
    template <typename V>
    static void At(impl::ArrayImpl& a, size_t place, const V& value) {
        Elements<E>(a)[place] = Element<E>::From(value);
    }
};

template <>
struct Store<MATLABString, false> {
    template <typename V>
    static void At(impl::ArrayImpl& a, size_t place, const V& value) {
        a.strings[place] = Element<MATLABString>::From(value);
    }
};

template <>
struct Store<Array, false> {
    template <typename V>
    static void At(impl::ArrayImpl& a, size_t place, const V& value) {
        a.elements[place] = Element<Array>::From(value);
    }
};

/// Fills an array from begin to end: in storage order, or reading row-major input into column-major
/// storage; values past its elements are not read, and elements past the values keep their default
/// (local: init.fewer, init.more, init.iter.rowmajor).
template <typename E, typename It>
inline void Fill(TypedArray<E>& made, It begin, const It& end, InputLayout layout) {
    impl::ArrayImpl& a = Access::writable(made);
    size_t n = getNumElements(a.dims);
    size_t k = 0;
    for (It it = begin; it != end && k < n; ++it, ++k) {
        size_t place = layout == InputLayout::ROW_MAJOR ? ColumnPlaceOfRowOrder(a.dims, k) : k;
        Store<E>::At(a, place, *it);
    }
}

/// A value for a cell made from values: an Array as it is, anything else as the scalar the factory makes of it.
template <typename T, bool IsArray = std::is_base_of<Array, T>::value>
struct CellValue {
    static Array Of(ArrayFactory& factory, const T& value);
};

template <typename T>
struct CellValue<T, true> {
    static Array Of(ArrayFactory&, const T& value) { return value; }
};

} // namespace detail

// --- The factory ---------------------------------------------------------------------------------

/// Makes arrays: of a type and size with every element its default, filled from values or a
/// buffer, scalars, text, cells, structs, enumerations and sparse matrices.
class ArrayFactory {
public:
    ArrayFactory() {}
    ~ArrayFactory() noexcept {}

    template <typename T>
    TypedArray<T> createArray(ArrayDimensions dims) {
        return detail::Access::createObj<TypedArray<T>>(detail::NewStorage(GetArrayType<T>::type, detail::Shape(std::move(dims))));
    }

    template <typename DATA_IT, typename T = typename std::remove_cv<typename std::iterator_traits<DATA_IT>::value_type>::type,
              typename ARRAY_TYPE = typename GetReturnType<T>::type>
    typename std::enable_if<!std::is_same<T, Object>::value, TypedArray<ARRAY_TYPE>>::type createArray(
        ArrayDimensions dims, DATA_IT begin, const DATA_IT& end, InputLayout inputLayout = InputLayout::COLUMN_MAJOR) {
        TypedArray<ARRAY_TYPE> made = createArray<ARRAY_TYPE>(std::move(dims));
        detail::Fill(made, begin, end, inputLayout);
        return made;
    }

    /// An object array of objects taken from other object arrays: every one of one class, as many as the array holds.
    template <typename DATA_IT, typename T = typename std::remove_cv<typename std::iterator_traits<DATA_IT>::value_type>::type,
              typename ARRAY_TYPE = typename GetReturnType<T>::type>
    typename std::enable_if<std::is_same<T, Object>::value, TypedArray<ARRAY_TYPE>>::type createArray(
        ArrayDimensions dims, DATA_IT begin, const DATA_IT& end, InputLayout inputLayout = InputLayout::COLUMN_MAJOR) {
        dims = detail::Shape(std::move(dims));
        std::vector<Object> objects(begin, end);
        size_t n = getNumElements(dims);
        if (objects.size() != n) {
            throw InvalidNumberOfElementsProvidedException("The number of objects given, " + std::to_string(objects.size()) +
                                                           ", is not the number of elements of the array, " + std::to_string(n) + ".");
        }
        std::shared_ptr<impl::ArrayImpl> a = detail::NewStorage(ArrayType::OBJECT, dims);
        a->objects = objects;
        for (size_t k = 0; k < n; k++) {
            const std::shared_ptr<impl::ObjectImpl>& o = detail::Access::getImpl(objects[k]);
            const std::shared_ptr<impl::ObjectImpl>& first = detail::Access::getImpl(objects[0]);
            if (!o || !first || o->className != first->className) {
                throw ObjectArrayIncompatibleTypesException(
                    "ObjectArray not created. Elements must belong to the same class, or to classes that are part of a heterogeneous hierarchy.");
            }
            size_t place = inputLayout == InputLayout::ROW_MAJOR ? detail::ColumnPlaceOfRowOrder(dims, k) : k;
            a->objects[place] = objects[k];
        }
        if (n > 0) {
            a->type = detail::Access::getImpl(objects[0])->type;
            a->className = detail::Access::getImpl(objects[0])->className;
        }
        return detail::Access::createObj<TypedArray<ARRAY_TYPE>>(std::move(a));
    }

    template <typename T>
    TypedArray<typename GetReturnType<T>::type> createArray(ArrayDimensions dims, std::initializer_list<T> data) {
        return createArray(std::move(dims), data.begin(), data.end());
    }

    template <typename T>
    typename std::enable_if<std::is_arithmetic<T>::value || is_complex<T>::value, TypedArray<T>>::type createArray(
        ArrayDimensions dims, const T* const begin, const T* const end) {
        TypedArray<T> made = createArray<T>(std::move(dims));
        detail::Fill(made, begin, end, InputLayout::COLUMN_MAJOR);
        return made;
    }

    template <typename T>
    typename std::enable_if<std::is_arithmetic<T>::value, TypedArray<T>>::type createScalar(const T val) {
        TypedArray<T> made = createArray<T>(ArrayDimensions{1, 1});
        detail::Elements<T>(*detail::Access::getImpl(made))[0] = val;
        return made;
    }

    template <typename T>
    typename std::enable_if<is_complex<T>::value, TypedArray<T>>::type createScalar(const T val) {
        TypedArray<T> made = createArray<T>(ArrayDimensions{1, 1});
        detail::Elements<T>(*detail::Access::getImpl(made))[0] = val;
        return made;
    }

    TypedArray<MATLABString> createScalar(const String val) { return createScalar(MATLABString(val)); }

    TypedArray<MATLABString> createScalar(const MATLABString val) {
        TypedArray<MATLABString> made = createArray<MATLABString>(ArrayDimensions{1, 1});
        detail::Access::getImpl(made)->strings[0] = val;
        return made;
    }

    /// A string scalar from ASCII text; any other byte is refused (local: scalar.std.nonascii).
    TypedArray<MATLABString> createScalar(const std::string& val) { return createScalar(MATLABString(detail::FromAscii(val))); }

    ObjectArray createScalar(const Object& val) {
        const std::shared_ptr<impl::ObjectImpl>& o = detail::Access::getImpl(val);
        std::shared_ptr<impl::ArrayImpl> a = detail::NewStorage(o ? o->type : ArrayType::OBJECT, ArrayDimensions{1, 1});
        a->objects.push_back(val);
        if (o) {
            a->className = o->className;
        }
        return detail::Access::createObj<ObjectArray>(std::move(a));
    }

    CellArray createCellArray(ArrayDimensions dims) { return createArray<Array>(std::move(dims)); }

    template <typename... Targs>
    TypedArray<Array> createCellArray(ArrayDimensions dims, Targs... data) {
        CellArray made = createCellArray(std::move(dims));
        Array values[] = {detail::CellValue<Targs>::Of(*this, data)...};
        impl::ArrayImpl& a = detail::Access::writable(made);
        for (size_t k = 0; k < sizeof...(Targs) && k < a.elements.size(); k++) {
            a.elements[k] = values[k];
        }
        return made;
    }

    /// A char row of the text; empty text is 0-by-0 (local: char.empty).
    CharArray createCharArray(String str) {
        std::shared_ptr<impl::ArrayImpl> a = detail::NewStorage(ArrayType::CHAR, str.empty() ? ArrayDimensions{0, 0} : ArrayDimensions{1, str.size()});
        if (!str.empty()) {
            std::memcpy(a->data, str.data(), str.size() * sizeof(char16_t));
        }
        return detail::Access::createObj<CharArray>(std::move(a));
    }

    CharArray createCharArray(const std::string& str) { return createCharArray(detail::FromAscii(str)); }

    CharArray createCharArrayFromUTF8(const std::string& str) { return createCharArray(detail::FromUtf8(str)); }

    /// A struct array with these fields, each []. Two fields of one name are refused; R2025b's API
    /// checks no other rule of a field's name (local: struct.illegal.*), and neither does this.
    StructArray createStructArray(ArrayDimensions dims, std::vector<std::string> fieldNames) {
        for (size_t i = 0; i < fieldNames.size(); i++) {
            for (size_t j = i + 1; j < fieldNames.size(); j++) {
                if (fieldNames[i] == fieldNames[j]) {
                    throw DuplicateFieldNameInStructArrayException("Duplicate field names not allowed.");
                }
            }
        }
        std::shared_ptr<impl::ArrayImpl> a = detail::NewStorage(ArrayType::STRUCT, detail::Shape(std::move(dims)), fieldNames.size());
        a->fields = std::move(fieldNames);
        return detail::Access::createObj<StructArray>(std::move(a));
    }

    EnumArray createEnumArray(ArrayDimensions dims, std::string className, std::vector<std::string> enums) {
        EnumArray made = createEnumArray(std::move(dims), std::move(className));
        impl::ArrayImpl& a = *detail::Access::getImpl(made);
        if (enums.size() != a.members.size()) {
            throw WrongNumberOfEnumsSuppliedException("Wrong number of enums supplied.");
        }
        a.members = std::move(enums);
        return made;
    }

    EnumArray createEnumArray(ArrayDimensions dims, std::string className) {
        if (className.empty()) {
            throw MustSpecifyClassNameException("Must specify a class name.");
        }
        std::shared_ptr<impl::ArrayImpl> a = detail::NewStorage(ArrayType::ENUM, detail::Shape(std::move(dims)));
        a->className = std::move(className);
        return detail::Access::createObj<EnumArray>(std::move(a));
    }

    Array createEmptyArray() { return detail::EmptyDouble(); }

    /// Memory for an array's elements, the caller's until it is given to createArrayFromBuffer.
    template <typename T>
    buffer_ptr_t<T> createBuffer(size_t numberOfElements) {
        return Buffer<T>(numberOfElements, detail::IsDense<T>());
    }

    /// An array that takes over a buffer's memory, read as stored in the layout given.
    template <typename T>
    TypedArray<T> createArrayFromBuffer(ArrayDimensions dims, buffer_ptr_t<T> buffer, MemoryLayout memoryLayout = MemoryLayout::COLUMN_MAJOR) {
        static_assert(detail::IsDense<T>::value, "Only numbers, characters and logicals come in a buffer.");
        if (memoryLayout == MemoryLayout::UNKNOWN) {
            throw InvalidMemoryLayoutException("Memory layout mismatch.");
        }
        std::shared_ptr<impl::ArrayImpl> a = std::make_shared<impl::ArrayImpl>();
        a->type = GetArrayType<T>::type;
        a->dims = detail::Shape(std::move(dims));
        a->layout = memoryLayout;
        a->dense = impl::KindOf<T>();
        a->count = getNumElements(a->dims);
        a->deleter = reinterpret_cast<void (*)()>(buffer.get_deleter());
        a->data = buffer.release();
        if (a->data == nullptr) {
            a->deleter = a->dense->deleteArray;
            a->data = a->dense->make(a->count);
        }
        return detail::Access::createObj<TypedArray<T>>(std::move(a));
    }

    template <typename T>
    typename std::enable_if<std::is_same<double, T>::value || std::is_same<bool, T>::value || std::is_same<std::complex<double>, T>::value,
                            SparseArray<T>>::type
    createSparseArray(ArrayDimensions dims, size_t nnz, buffer_ptr_t<T> data, buffer_ptr_t<size_t> rows, buffer_ptr_t<size_t> cols) {
        return Sparse<T>(std::move(dims), nnz, data.get(), rows.get(), cols.get());
    }

    template <typename T>
    typename std::enable_if<std::is_same<float, T>::value || std::is_same<std::complex<float>, T>::value, SparseArray<T>>::type
    createSparseArray(ArrayDimensions dims, size_t nnz, buffer_ptr_t<T> data, buffer_ptr_t<size_t> rows, buffer_ptr_t<size_t> cols) {
        return Sparse<T>(std::move(dims), nnz, data.get(), rows.get(), cols.get());
    }

private:
    ArrayFactory& operator=(ArrayFactory const& rhs) = delete;
    ArrayFactory(const ArrayFactory& rhs) = delete;

    template <typename T>
    static buffer_ptr_t<T> Buffer(size_t n, std::true_type) {
        return buffer_ptr_t<T>(n == 0 ? nullptr : new T[n](), &detail::DeleteArray<T>);
    }

    template <typename T>
    static buffer_ptr_t<T> Buffer(size_t, std::false_type) {
        detail::ThrowMismatch();
    }

    /// A sparse matrix of the values at (row, column): two dimensions only, every place inside them,
    /// stored by column and row as R2025b stores them (local: sparse.unsorted), a place given twice summed.
    template <typename T>
    static SparseArray<T> Sparse(ArrayDimensions dims, size_t nnz, const T* values, const size_t* rows, const size_t* cols) {
        if (dims.size() != 2) {
            throw InvalidDimensionsInSparseArrayException("Sparse Array can only have 2 dimensions");
        }
        std::vector<size_t> order(nnz);
        for (size_t k = 0; k < nnz; k++) {
            order[k] = k;
            if (rows[k] >= dims[0]) {
                throw std::runtime_error("Row Range Check");
            }
            if (cols[k] >= dims[1]) {
                throw std::runtime_error("Column Range Check");
            }
        }
        std::stable_sort(order.begin(), order.end(), [&](size_t x, size_t y) {
            return cols[x] != cols[y] ? cols[x] < cols[y] : rows[x] < rows[y];
        });
        std::shared_ptr<impl::ArrayImpl> a = detail::NewStorage(GetSparseArrayType<T>::type, dims);
        std::vector<T> kept;
        for (size_t k : order) {
            if (!a->rows.empty() && a->rows.back() == rows[k] && !kept.empty() && LastColumn(*a) == cols[k]) {
                kept.back() = Sum(kept.back(), values[k]);
                continue;
            }
            a->rows.push_back(rows[k]);
            kept.push_back(values[k]);
            for (size_t c = cols[k] + 1; c < a->columnStarts.size(); c++) {
                a->columnStarts[c] = a->rows.size();
            }
        }
        a->count = kept.size();
        a->data = a->dense->make(a->count);
        std::copy(kept.begin(), kept.end(), static_cast<T*>(a->data));
        return detail::Access::createObj<SparseArray<T>>(std::move(a));
    }

    static size_t LastColumn(const impl::ArrayImpl& a) {
        size_t last = a.rows.size() - 1;
        size_t column = 0;
        while (column + 1 < a.columnStarts.size() && a.columnStarts[column + 1] <= last) {
            column++;
        }
        return column;
    }

    template <typename T>
    static T Sum(const T& x, const T& y) {
        return x + y;
    }
    static bool Sum(bool x, bool y) { return x || y; }
};

namespace detail {

template <typename T, bool IsArray>
inline Array CellValue<T, IsArray>::Of(ArrayFactory& factory, const T& value) {
    return factory.createScalar(value);
}

} // namespace detail

// --- Ordered iterators ---------------------------------------------------------------------------

namespace detail {

struct ColumnMajorOrder {};
struct RowMajorOrder {};

/// An iterator over an array's elements in column-major or row-major order, whatever the storage's.
template <typename T, typename Order>
class OrderedIterator {
public:
    typedef typename IteratorKinds<T>::impl_type impl_type;
    typedef typename IteratorKinds<T>::reference reference;
    typedef typename IteratorKinds<T>::pointer pointer;
    typedef std::random_access_iterator_tag iterator_category;
    typedef T value_type;
    typedef std::ptrdiff_t difference_type;

    OrderedIterator() noexcept : fIndex(0) {}
    OrderedIterator(std::shared_ptr<impl::ArrayImpl> owner, size_t index) noexcept : fOwner(std::move(owner)), fIndex(index) {}
    OrderedIterator& operator++() noexcept {
        ++fIndex;
        return *this;
    }
    OrderedIterator& operator--() noexcept {
        --fIndex;
        return *this;
    }
    OrderedIterator operator++(int) noexcept {
        OrderedIterator before(*this);
        ++fIndex;
        return before;
    }
    OrderedIterator operator--(int) noexcept {
        OrderedIterator before(*this);
        --fIndex;
        return before;
    }
    OrderedIterator& operator+=(difference_type d) noexcept {
        fIndex = static_cast<size_t>(static_cast<difference_type>(fIndex) + d);
        return *this;
    }
    OrderedIterator& operator-=(difference_type d) noexcept {
        fIndex = static_cast<size_t>(static_cast<difference_type>(fIndex) - d);
        return *this;
    }
    OrderedIterator operator+(difference_type d) const noexcept {
        OrderedIterator moved(*this);
        moved += d;
        return moved;
    }
    OrderedIterator operator-(difference_type d) const noexcept {
        OrderedIterator moved(*this);
        moved -= d;
        return moved;
    }
    difference_type operator-(const OrderedIterator& rhs) const noexcept {
        return static_cast<difference_type>(fIndex) - static_cast<difference_type>(rhs.fIndex);
    }
    bool operator==(const OrderedIterator& rhs) const noexcept { return fIndex == rhs.fIndex; }
    bool operator!=(const OrderedIterator& rhs) const noexcept { return fIndex != rhs.fIndex; }
    bool operator<(const OrderedIterator& rhs) const noexcept { return fIndex < rhs.fIndex; }
    bool operator>(const OrderedIterator& rhs) const noexcept { return fIndex > rhs.fIndex; }
    bool operator<=(const OrderedIterator& rhs) const noexcept { return fIndex <= rhs.fIndex; }
    bool operator>=(const OrderedIterator& rhs) const noexcept { return fIndex >= rhs.fIndex; }
    reference operator*() const { return Access::createIterator<T>(fOwner, Place())[0]; }
    reference operator[](size_t k) const { return *(*this + static_cast<difference_type>(k)); }

private:
    size_t Place() const {
        const impl::ArrayImpl& a = *fOwner;
        bool columnOrder = std::is_same<Order, ColumnMajorOrder>::value;
        bool columnStorage = a.layout != MemoryLayout::ROW_MAJOR;
        if (columnOrder == columnStorage) {
            return fIndex;
        }
        return columnOrder ? RowPlaceOfColumnOrder(a.dims, fIndex) : ColumnPlaceOfRowOrder(a.dims, fIndex);
    }
    std::shared_ptr<impl::ArrayImpl> fOwner;
    size_t fIndex;
};

template <typename T>
inline const std::shared_ptr<impl::ArrayImpl>& HeldAs(const Array& arr) {
    const std::shared_ptr<impl::ArrayImpl>& held = Access::getImpl(arr);
    if (!held || !Holds<T>(held->type)) {
        ThrowTypedArray();
    }
    return held;
}

template <typename T>
inline std::shared_ptr<impl::ArrayImpl> WritableAs(Array& arr) {
    HeldAs<T>(arr);
    Access::writable(arr);
    return Access::getImpl(arr);
}

template <typename T>
inline std::shared_ptr<impl::ArrayImpl> HeldAs(const RefImpl& ref, bool writable) {
    std::shared_ptr<impl::ArrayImpl> held = SlotStorage(ref, writable);
    if (!Holds<T>(held->type)) {
        ThrowTypedArray();
    }
    return held;
}

/// Iterators and element ranges in one order, over arrays and the arrays references hold.
template <template <class> class IteratorType>
class IteratorFactory {
public:
    template <typename T>
    static IteratorType<T> begin(Array& arr) {
        return IteratorType<T>(WritableAs<T>(arr), 0);
    }
    template <typename T>
    static IteratorType<T> begin(TypedArray<T>& arr) {
        return IteratorType<T>(WritableAs<T>(arr), 0);
    }
    template <typename T>
    static IteratorType<T> begin(Reference<Array>& ref) {
        return IteratorType<T>(HeldAs<T>(*Access::getRef(ref), true), 0);
    }
    template <typename T>
    static IteratorType<typename std::add_const<T>::type> cbegin(const Array& arr) {
        return IteratorType<typename std::add_const<T>::type>(HeldAs<T>(arr), 0);
    }
    template <typename T>
    static IteratorType<typename std::add_const<T>::type> begin(const Array& arr) {
        return cbegin<T>(arr);
    }
    template <typename T>
    static IteratorType<T> end(Array& arr) {
        std::shared_ptr<impl::ArrayImpl> held = WritableAs<T>(arr);
        size_t n = getNumElements(held->dims);
        return IteratorType<T>(std::move(held), n);
    }
    template <typename T>
    static IteratorType<T> end(TypedArray<T>& arr) {
        return end<T>(static_cast<Array&>(arr));
    }
    template <typename T>
    static IteratorType<T> end(Reference<Array>& ref) {
        std::shared_ptr<impl::ArrayImpl> held = HeldAs<T>(*Access::getRef(ref), true);
        size_t n = getNumElements(held->dims);
        return IteratorType<T>(std::move(held), n);
    }
    template <typename T>
    static IteratorType<typename std::add_const<T>::type> cend(const Array& arr) {
        const std::shared_ptr<impl::ArrayImpl>& held = HeldAs<T>(arr);
        return IteratorType<typename std::add_const<T>::type>(held, getNumElements(held->dims));
    }
    template <typename T>
    static IteratorType<typename std::add_const<T>::type> end(const Array& arr) {
        return cend<T>(arr);
    }

    template <typename T>
    static Range<IteratorType, typename std::add_const<T>::type> readOnlyElements(const Array& arr) {
        return Range<IteratorType, typename std::add_const<T>::type>(cbegin<T>(arr), cend<T>(arr));
    }
    template <typename T>
    static Range<IteratorType, typename std::add_const<T>::type> readOnlyElements(const TypedArray<T>& arr) {
        return readOnlyElements<T>(static_cast<const Array&>(arr));
    }
    template <typename T>
    static Range<IteratorType, typename std::add_const<T>::type> readOnlyElements(const Reference<Array>& ref) {
        std::shared_ptr<impl::ArrayImpl> held = HeldAs<T>(*Access::getRef(ref), false);
        size_t n = getNumElements(held->dims);
        return Range<IteratorType, typename std::add_const<T>::type>(IteratorType<typename std::add_const<T>::type>(held, 0),
                                                                     IteratorType<typename std::add_const<T>::type>(held, n));
    }
    template <typename T>
    static Range<IteratorType, T> writableElements(TypedArray<T>& arr) {
        return writableElements<T>(static_cast<Array&>(arr));
    }
    template <typename T>
    static Range<IteratorType, T> writableElements(Array& arr) {
        std::shared_ptr<impl::ArrayImpl> held = WritableAs<T>(arr);
        size_t n = getNumElements(held->dims);
        return Range<IteratorType, T>(IteratorType<T>(held, 0), IteratorType<T>(held, n));
    }
    template <typename T>
    static Range<IteratorType, T> writableElements(Reference<Array>& ref) {
        std::shared_ptr<impl::ArrayImpl> held = HeldAs<T>(*Access::getRef(ref), true);
        size_t n = getNumElements(held->dims);
        return Range<IteratorType, T>(IteratorType<T>(held, 0), IteratorType<T>(held, n));
    }
};

} // namespace detail

template <typename T>
using ColumnMajorIterator = detail::OrderedIterator<T, detail::ColumnMajorOrder>;
template <typename T>
using RowMajorIterator = detail::OrderedIterator<T, detail::RowMajorOrder>;
using ColumnMajor = detail::IteratorFactory<ColumnMajorIterator>;
using RowMajor = detail::IteratorFactory<RowMajorIterator>;

// --- Element ranges in storage order --------------------------------------------------------------

template <typename T>
inline Range<TypedIterator, T const> getReadOnlyElements(const Array& arr) {
    const std::shared_ptr<impl::ArrayImpl>& held = detail::HeldAs<T>(arr);
    return Range<TypedIterator, T const>(detail::Access::createIterator<T const>(held, 0),
                                         detail::Access::createIterator<T const>(held, getNumElements(held->dims)));
}

template <typename T>
inline Range<TypedIterator, T> getWritableElements(Array& arr) {
    std::shared_ptr<impl::ArrayImpl> held = detail::WritableAs<T>(arr);
    size_t n = getNumElements(held->dims);
    return Range<TypedIterator, T>(detail::Access::createIterator<T>(held, 0), detail::Access::createIterator<T>(held, n));
}

template <typename T>
inline Range<TypedIterator, T const> getReadOnlyElements(const Reference<Array>& ref) {
    std::shared_ptr<impl::ArrayImpl> held = detail::HeldAs<T>(*detail::Access::getRef(ref), false);
    size_t n = getNumElements(held->dims);
    return Range<TypedIterator, T const>(detail::Access::createIterator<T const>(held, 0), detail::Access::createIterator<T const>(held, n));
}

template <typename T>
inline Range<TypedIterator, T> getWritableElements(Reference<Array>& ref) {
    std::shared_ptr<impl::ArrayImpl> held = detail::HeldAs<T>(*detail::Access::getRef(ref), true);
    size_t n = getNumElements(held->dims);
    return Range<TypedIterator, T>(detail::Access::createIterator<T>(held, 0), detail::Access::createIterator<T>(held, n));
}

// --- Visitors ------------------------------------------------------------------------------------

namespace detail {

template <typename V, typename A>
inline auto Visit(V& visitor, A arr) -> decltype(visitor(arr)) {
    return visitor(arr);
}

} // namespace detail

/// Calls the visitor with the array as the typed array its type is: a CharArray, a TypedArray<T> of
/// numbers, logicals or strings, a cell, a StructArray or an EnumArray. Any other type is refused.
template <typename A, typename V>
auto apply_visitor(A&& a, V&& visitor) -> decltype(visitor(std::declval<CharArray&>())) {
    const Array& arr = a;
    switch (arr.getType()) {
    case ArrayType::CHAR: return detail::Visit<V, CharArray>(visitor, CharArray(arr));
    case ArrayType::MATLAB_STRING: return detail::Visit<V, TypedArray<MATLABString>>(visitor, TypedArray<MATLABString>(arr));
    case ArrayType::LOGICAL: return detail::Visit<V, TypedArray<bool>>(visitor, TypedArray<bool>(arr));
    case ArrayType::DOUBLE: return detail::Visit<V, TypedArray<double>>(visitor, TypedArray<double>(arr));
    case ArrayType::SINGLE: return detail::Visit<V, TypedArray<float>>(visitor, TypedArray<float>(arr));
    case ArrayType::INT8: return detail::Visit<V, TypedArray<int8_t>>(visitor, TypedArray<int8_t>(arr));
    case ArrayType::UINT8: return detail::Visit<V, TypedArray<uint8_t>>(visitor, TypedArray<uint8_t>(arr));
    case ArrayType::INT16: return detail::Visit<V, TypedArray<int16_t>>(visitor, TypedArray<int16_t>(arr));
    case ArrayType::UINT16: return detail::Visit<V, TypedArray<uint16_t>>(visitor, TypedArray<uint16_t>(arr));
    case ArrayType::INT32: return detail::Visit<V, TypedArray<int32_t>>(visitor, TypedArray<int32_t>(arr));
    case ArrayType::UINT32: return detail::Visit<V, TypedArray<uint32_t>>(visitor, TypedArray<uint32_t>(arr));
    case ArrayType::INT64: return detail::Visit<V, TypedArray<int64_t>>(visitor, TypedArray<int64_t>(arr));
    case ArrayType::UINT64: return detail::Visit<V, TypedArray<uint64_t>>(visitor, TypedArray<uint64_t>(arr));
    case ArrayType::COMPLEX_DOUBLE: return detail::Visit<V, TypedArray<std::complex<double>>>(visitor, TypedArray<std::complex<double>>(arr));
    case ArrayType::COMPLEX_SINGLE: return detail::Visit<V, TypedArray<std::complex<float>>>(visitor, TypedArray<std::complex<float>>(arr));
    case ArrayType::COMPLEX_INT8: return detail::Visit<V, TypedArray<std::complex<int8_t>>>(visitor, TypedArray<std::complex<int8_t>>(arr));
    case ArrayType::COMPLEX_UINT8: return detail::Visit<V, TypedArray<std::complex<uint8_t>>>(visitor, TypedArray<std::complex<uint8_t>>(arr));
    case ArrayType::COMPLEX_INT16: return detail::Visit<V, TypedArray<std::complex<int16_t>>>(visitor, TypedArray<std::complex<int16_t>>(arr));
    case ArrayType::COMPLEX_UINT16: return detail::Visit<V, TypedArray<std::complex<uint16_t>>>(visitor, TypedArray<std::complex<uint16_t>>(arr));
    case ArrayType::COMPLEX_INT32: return detail::Visit<V, TypedArray<std::complex<int32_t>>>(visitor, TypedArray<std::complex<int32_t>>(arr));
    case ArrayType::COMPLEX_UINT32: return detail::Visit<V, TypedArray<std::complex<uint32_t>>>(visitor, TypedArray<std::complex<uint32_t>>(arr));
    case ArrayType::COMPLEX_INT64: return detail::Visit<V, TypedArray<std::complex<int64_t>>>(visitor, TypedArray<std::complex<int64_t>>(arr));
    case ArrayType::COMPLEX_UINT64: return detail::Visit<V, TypedArray<std::complex<uint64_t>>>(visitor, TypedArray<std::complex<uint64_t>>(arr));
    case ArrayType::CELL: return detail::Visit<V, TypedArray<Array>>(visitor, TypedArray<Array>(arr));
    case ArrayType::STRUCT: return detail::Visit<V, StructArray>(visitor, StructArray(arr));
    case ArrayType::ENUM: return detail::Visit<V, EnumArray>(visitor, EnumArray(arr));
    default:
        throw InvalidArrayTypeException("Array type mismatch.");
    }
}

/// The same for numeric arrays only.
template <typename A, typename V>
auto apply_numeric_visitor(A&& a, V&& visitor) -> decltype(visitor(std::declval<TypedArray<double>&>())) {
    const Array& arr = a;
    switch (arr.getType()) {
    case ArrayType::DOUBLE: return detail::Visit<V, TypedArray<double>>(visitor, TypedArray<double>(arr));
    case ArrayType::SINGLE: return detail::Visit<V, TypedArray<float>>(visitor, TypedArray<float>(arr));
    case ArrayType::INT8: return detail::Visit<V, TypedArray<int8_t>>(visitor, TypedArray<int8_t>(arr));
    case ArrayType::UINT8: return detail::Visit<V, TypedArray<uint8_t>>(visitor, TypedArray<uint8_t>(arr));
    case ArrayType::INT16: return detail::Visit<V, TypedArray<int16_t>>(visitor, TypedArray<int16_t>(arr));
    case ArrayType::UINT16: return detail::Visit<V, TypedArray<uint16_t>>(visitor, TypedArray<uint16_t>(arr));
    case ArrayType::INT32: return detail::Visit<V, TypedArray<int32_t>>(visitor, TypedArray<int32_t>(arr));
    case ArrayType::UINT32: return detail::Visit<V, TypedArray<uint32_t>>(visitor, TypedArray<uint32_t>(arr));
    case ArrayType::INT64: return detail::Visit<V, TypedArray<int64_t>>(visitor, TypedArray<int64_t>(arr));
    case ArrayType::UINT64: return detail::Visit<V, TypedArray<uint64_t>>(visitor, TypedArray<uint64_t>(arr));
    case ArrayType::COMPLEX_DOUBLE: return detail::Visit<V, TypedArray<std::complex<double>>>(visitor, TypedArray<std::complex<double>>(arr));
    case ArrayType::COMPLEX_SINGLE: return detail::Visit<V, TypedArray<std::complex<float>>>(visitor, TypedArray<std::complex<float>>(arr));
    default:
        throw InvalidArrayTypeException("Array type mismatch.");
    }
}

/// Calls the visitor with a reference to the array a cell or field holds, as the typed reference its type is.
template <typename V>
auto apply_visitor_ref(const ArrayRef& a, V visitor) -> decltype(visitor(std::declval<CharArrayRef&>())) {
    switch (a.getType()) {
    case ArrayType::CHAR: return detail::Visit<V, CharArrayRef>(visitor, CharArrayRef(a));
    case ArrayType::MATLAB_STRING: return detail::Visit<V, TypedArrayRef<MATLABString>>(visitor, TypedArrayRef<MATLABString>(a));
    case ArrayType::LOGICAL: return detail::Visit<V, TypedArrayRef<bool>>(visitor, TypedArrayRef<bool>(a));
    case ArrayType::DOUBLE: return detail::Visit<V, TypedArrayRef<double>>(visitor, TypedArrayRef<double>(a));
    case ArrayType::SINGLE: return detail::Visit<V, TypedArrayRef<float>>(visitor, TypedArrayRef<float>(a));
    case ArrayType::INT8: return detail::Visit<V, TypedArrayRef<int8_t>>(visitor, TypedArrayRef<int8_t>(a));
    case ArrayType::UINT8: return detail::Visit<V, TypedArrayRef<uint8_t>>(visitor, TypedArrayRef<uint8_t>(a));
    case ArrayType::INT16: return detail::Visit<V, TypedArrayRef<int16_t>>(visitor, TypedArrayRef<int16_t>(a));
    case ArrayType::UINT16: return detail::Visit<V, TypedArrayRef<uint16_t>>(visitor, TypedArrayRef<uint16_t>(a));
    case ArrayType::INT32: return detail::Visit<V, TypedArrayRef<int32_t>>(visitor, TypedArrayRef<int32_t>(a));
    case ArrayType::UINT32: return detail::Visit<V, TypedArrayRef<uint32_t>>(visitor, TypedArrayRef<uint32_t>(a));
    case ArrayType::INT64: return detail::Visit<V, TypedArrayRef<int64_t>>(visitor, TypedArrayRef<int64_t>(a));
    case ArrayType::UINT64: return detail::Visit<V, TypedArrayRef<uint64_t>>(visitor, TypedArrayRef<uint64_t>(a));
    case ArrayType::COMPLEX_DOUBLE: return detail::Visit<V, TypedArrayRef<std::complex<double>>>(visitor, TypedArrayRef<std::complex<double>>(a));
    case ArrayType::COMPLEX_SINGLE: return detail::Visit<V, TypedArrayRef<std::complex<float>>>(visitor, TypedArrayRef<std::complex<float>>(a));
    case ArrayType::CELL: return detail::Visit<V, CellArrayRef>(visitor, CellArrayRef(a));
    case ArrayType::STRUCT: return detail::Visit<V, StructArrayRef>(visitor, StructArrayRef(a));
    case ArrayType::ENUM: return detail::Visit<V, EnumArrayRef>(visitor, EnumArrayRef(a));
    default:
        throw InvalidArrayTypeException("Array type mismatch.");
    }
}

// --- Comparisons of references -------------------------------------------------------------------

namespace detail {

inline MATLABString ValueOf(Reference<MATLABString> const& ref) { return Read<MATLABString>(*Access::getRef(ref)); }

} // namespace detail

inline bool operator==(Reference<MATLABString> const& lhs, MATLABString const& rhs) noexcept { return detail::ValueOf(lhs) == rhs; }
inline bool operator==(MATLABString const& lhs, Reference<MATLABString> const& rhs) noexcept { return rhs == lhs; }
inline bool operator==(Reference<MATLABString> const& lhs, String const& rhs) noexcept {
    MATLABString s = detail::ValueOf(lhs);
    return s.has_value() && *s == rhs;
}
inline bool operator==(String const& lhs, Reference<MATLABString> const& rhs) noexcept { return rhs == lhs; }
inline bool operator==(Reference<MATLABString> const& lhs, std::string const& rhs) noexcept {
    MATLABString s = detail::ValueOf(lhs);
    if (!s.has_value() || s->size() != rhs.size()) {
        return false;
    }
    for (size_t k = 0; k < rhs.size(); k++) {
        if ((*s)[k] != static_cast<char16_t>(static_cast<unsigned char>(rhs[k]))) {
            return false;
        }
    }
    return true;
}
inline bool operator==(std::string const& lhs, Reference<MATLABString> const& rhs) noexcept { return rhs == lhs; }
inline bool operator==(Reference<MATLABString> const& lhs, Reference<MATLABString> const& rhs) noexcept {
    return detail::ValueOf(lhs) == detail::ValueOf(rhs);
}

template <typename T>
inline bool operator==(Reference<T> const& lhs, T const& rhs) noexcept {
    return static_cast<T>(lhs) == rhs;
}
template <typename T>
inline bool operator==(T const& lhs, Reference<T> const& rhs) noexcept {
    return rhs == lhs;
}
template <typename T>
inline bool operator==(Reference<T> const& lhs, Reference<T> const& rhs) noexcept {
    return static_cast<T>(lhs) == static_cast<T>(rhs);
}
template <typename T, typename U>
inline bool operator!=(Reference<T> const& lhs, U const& rhs) noexcept {
    return !(lhs == rhs);
}

} // namespace data
} // namespace jgraph

#endif // JGRAPH_DATA_ARRAY_IMPL_HPP
