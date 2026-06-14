"""Exploratory TSWD decoder: dump variable records + their type records to crack the
type encoding. Uses the address map for ground-truth global names."""
import sys, struct

def u32(b,o): return struct.unpack_from('<I',b,o)[0]
def i32(b,o): return struct.unpack_from('<i',b,o)[0]
def u16(b,o): return struct.unpack_from('<H',b,o)[0]

def find_blob(path):
    d=open(path,'rb').read()
    lfa=u32(d,0x3c); nsec=u16(d,lfa+6); optsz=u16(d,lfa+20); st=lfa+24+optsz
    for i in range(nsec):
        o=st+i*40
        if d[o:o+8].rstrip(b'\0')==b'.cwdebug':
            rp=u32(d,o+20); loc=d[rp:rp+32]
            assert loc[12:16]==b'TSWD'
            return d[u32(loc,24):u32(loc,24)+u32(loc,16)]
    return None

def cstr(b,o):
    e=b.index(0,o); return b[o:e].decode('latin1')

blob=find_blob(sys.argv[1])
dirv=[u32(blob,8+4*i) for i in range(12)]
print("dir:",[hex(x) for x in dirv])
name_base=dirv[6]; sym_base=dirv[8]; am_off=dirv[11]; am_cnt=dirv[10]
print(f"name_base={name_base:#x} sym_base={sym_base:#x} am @{am_off:#x} cnt={am_cnt}")

# address map: (rva, ref-relative-to-sym_base)
print("\n=== address map (top-level symbols) ===")
amap={}
for i in range(am_cnt):
    rva=u32(blob,am_off+i*8); ref=u32(blob,am_off+i*8+4)
    amap[ref]=rva
    print(f"  rva={0x400000+rva:#010x}  ref={ref:#x} -> sym@{sym_base+ref:#x}")

def dump_typerec(tr, depth=0):
    """print raw bytes of a type record at stream offset tr"""
    o=sym_base+tr
    raw=blob[o:o+20]
    return ' '.join(f'{x:02x}' for x in raw)

# Walk every tag-0x04 variable record in the symbol stream.
print("\n=== variable records (tag 0x04: typeRef, nameOff, offset) ===")
o=sym_base
end=am_off
while o+13<=end:
    if blob[o]==0x04:
        typeRef=u32(blob,o+1); nameOff=u32(blob,o+5); off=i32(blob,o+9)
        # sanity: nameOff within name table region
        if nameOff < (sym_base-name_base):
            try: nm=cstr(blob,name_base+nameOff)
            except: nm='?'
            soff=o-sym_base
            kind = f"rva={0x400000+off:#x}" if off>0x1000 else f"frame={off}"
            print(f"  @{soff:#05x} {nm:10s} typeRef={typeRef:#06x} {kind:18s} | typerec[{typeRef:#x}]: {dump_typerec(typeRef)}")
            o+=13; continue
    o+=1
