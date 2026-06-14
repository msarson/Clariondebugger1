# TSWD debug format — record-level spec

Reverse-engineered from Clarion 12 debug builds (`vid=full`). This documents the symbol
and type records inside the `TSWD` blob (see `FINDINGS.md` for the blob locator/header).
Verified end-to-end: every type below reads correct live values from a running debuggee.

## Blob directory (dwords at blob+8)

| idx | meaning |
|----|---------|
| 1 | source-file name offset |
| 3 / 4 | line table offset / entry count |
| 6 | name string-table base (`NB`) |
| 8 | symbol-record stream base (`SB`) |
| 10 / 11 | address-map count / offset (`AM`) |

**Line table**: `{ u16 line; u32 rva }` × count.
**Address map**: `{ u32 rva; u32 ref }` × count — the top-level symbols (globals + procs).
**Names**: null-terminated at `NB + nameOff`. Globals exported `$NAME`, procs mangled `NAME@F<args>`.

## Universal record rule

Every *ref* (from the address map, a proc's local list, or a group's member list) addresses
a record whose **tag byte is at `SB + ref + 4`** (the 4 bytes before the tag are a link field).

### Variable record — tag `0x04` (global/local), `0x0c` (group member)
```
+4  u8   tag (0x04 / 0x0c)
+5  u32  typeRef        -> type record
+9  u32  nameOff        -> name table
+13 i32  offset         globals: RVA;  locals/params: signed frame offset from EBP;
                        group members: byte offset within the group
```

### Procedure record — tag `0x05`
```
+4  u8   tag (0x05)
+5  u32  retTypeRef     (0 if none)
+9  u32  nameOff
+13 u32  entryRva
+25 u32  localCount
+29 u32  localRef[localCount]    -> variable records (frame-offset locals/params)
```

### Type record (tag at `SB + typeRef + 4`)
```
0x11 int    : +5 u32 size            (SHORT=2, LONG=4)
0x12 uint   : +5 u32 size            (BYTE=1, USHORT=2, ULONG/DATE/TIME=4)
0x13 float  : +5 u32 size            (SREAL=4, REAL=8)
0x14 char   : +5 u32 size (=1)       (string element)
0x23 decimal: +5 u32 size, +9 u8 places   (packed BCD, e.g. DECIMAL(9,2)->size 5)
0x24 pdecim : +5 u32 size, +9 u8 places   (PDECIMAL(7,3)->size 4)
0x08 group  : +5 u32 size, +9 u32 count, +13 u32 memberRef[count]
0x18 arr/str: +5 u32 refA, +9 u32 refB, +13 u8 elemTag, +14 u32 elemSize,
              +18 u8 0x0f, +19 u32 nameref, +23 u32 numDims, +27 u32 length
              elemTag 0x14 -> STRING(length);  else ARRAY[length] of elem
```

## Value encoding notes
- **DECIMAL/PDECIMAL**: packed BCD, two digits/byte; a low sign nibble `0x0B/0x0D` = negative.
  Value = digits / 10^places. (Verified: `19.99`, `12.34`, `5.678`.)
- **STRING(n)**: fixed n bytes, space/null padded. **CSTRING**: null-terminated. **PSTRING**:
  first byte is the length (currently shown raw — refinement pending).
- **DATE/TIME**: stored as a Clarion serial `ULONG` (assignment goes through
  `Cla$storebtdate/bttime`); shown as the raw serial — calendar formatting pending.

## Open refinements
- Distinguish STRING / CSTRING / PSTRING via the `0x18` refA/refB sub-records.
- Calendar formatting for DATE/TIME serials.
- Param-vs-local flag (frame-offset sign is not a reliable discriminator in Clarion).
