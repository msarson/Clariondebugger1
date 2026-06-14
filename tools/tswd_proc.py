"""Decode procedure records + group records to find their member/local lists."""
import sys, struct
def u32(b,o): return struct.unpack_from('<I',b,o)[0]
def i32(b,o): return struct.unpack_from('<i',b,o)[0]
def u16(b,o): return struct.unpack_from('<H',b,o)[0]
def find_blob(path):
    d=open(path,'rb').read(); lfa=u32(d,0x3c); nsec=u16(d,lfa+6); optsz=u16(d,lfa+20); st=lfa+24+optsz
    for i in range(nsec):
        o=st+i*40
        if d[o:o+8].rstrip(b'\0')==b'.cwdebug':
            rp=u32(d,o+20); loc=d[rp:rp+32]; return d[u32(loc,24):u32(loc,24)+u32(loc,16)]
def cstr(b,o):
    e=b.index(0,o); return b[o:e].decode('latin1')

blob=find_blob(sys.argv[1])
dirv=[u32(blob,8+4*i) for i in range(12)]
NB=dirv[6]; SB=dirv[8]; AM=dirv[11]; AMC=dirv[10]
def name_at(n): return cstr(blob,NB+n)

print("=== top-level symbols from address map ===")
for i in range(AMC):
    rva=u32(blob,AM+i*8); ref=u32(blob,AM+i*8+4)
    o=SB+ref
    tag=blob[o]
    # raw 40 bytes of the record
    raw=' '.join(f'{x:02x}' for x in blob[o:o+44])
    print(f"\nrva={0x400000+rva:#x} ref={ref:#x} firstbyte={tag:#x}")
    print(f"  {raw}")
    # try to find name dword (nameOff) and any inner refs
