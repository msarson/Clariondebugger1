# TSWD debug format — record-level spec

Reverse-engineered from Clarion 12 debug builds (`vid=full`). This documents the symbol
and type records inside the `TSWD` blob (see `FINDINGS.md` for the blob locator/header).
Verified end-to-end: every type below reads correct live values from a running debuggee.

## Blob header + directory

```
blob+0  'TSWD'
blob+4  u32 moduleCount        (1 for single-file programs; 54 for the School ABC app)
blob+8  directory: 12 u32 stream offsets (relative to blob start)
```

| dir idx | meaning |
|----|---------|
| 0 | module name-offset array: `moduleCount` × u32 (offsets into dir[1]) |
| 1 | module name string table (`ABBROWSE.CLW\0SCHOOL.clw\0…`) |
| 2 | per-module line ranges: `moduleCount` × `{ u32 firstLineIdx; u32 lastLineIdx }` (`{0,0}`=no lines) |
| 3 / 4 | line table offset / total entry count |
| 6 | name string-table base (`NB`) |
| 8 | symbol-record stream base (`SB`) |
| 10 / 11 | address-map count / offset (`AM`) |

**Multi-module**: every module's lines live in the one flat line table; `dir[2]` gives each
module its `[firstLineIdx, lastLineIdx]` slice. So an RVA → `(module, line)` by finding the
line entry with the greatest rva ≤ target and reading back which module's slice owns it.
(Single-file programs are just `moduleCount==1`.)

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

## FILE / QUEUE / GROUP member layouts (records & browse queues)

A FILE record, QUEUE, or GROUP buffer is a container whose fields are emitted as **member
records** — tag `0x0c`, same field layout as a variable (`typeRef@+1`, `nameOff@+5`,
`offset@+9` = byte offset within the container) plus a **scope** dword at `+13`. All members of
one container share the same scope value, so grouping `0x0c` records by `+13` reconstructs each
structure's field list (name + offset), sorted by offset. Field *type* records here are not
reliably decodable, so element size is inferred from the gap to the next field's offset.

A container links to its member group through its own type ref:
- **FILE record buffers**: `scope == container.typeRef`. The buffer is a fixed global named
  `<FILE>$<pre>:RECORD` (e.g. `STUDENTS$STU:RECORD`), so a field address is `buffer_base + offset`.
- **QUEUEs**: `scope == container.typeRef + 5`. The QUEUE *local* is a 4-byte **handle**, not an
  inline buffer — dereference it to get the current element buffer, then index with the field
  group. ABC names the browse queue `QUEUE:BROWSE:<n>` for browse object `BRW<n>`, so
  `BRW1.Q.STU:LastName` resolves via `QUEUE:BROWSE:1`'s layout at the dereferenced buffer.

(Verified end-to-end against the School ABC app: `STUDENTS$STU:RECORD` → 11 fields at the exact
dictionary offsets; the BrowseStudents queue → 16 fields incl. `MAJ:Description`, `VIEWPOSITION`.)

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
