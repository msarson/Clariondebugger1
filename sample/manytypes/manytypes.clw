  PROGRAM

  MAP
DoCalc PROCEDURE(LONG pA, REAL pB),REAL
  END

vByte    BYTE
vShort   SHORT
vUShort  USHORT
vLong    LONG
vULong   ULONG
vSReal   SREAL
vReal    REAL
vDec     DECIMAL(9,2)
vPDec    PDECIMAL(7,3)
vStr     STRING(16)
vCStr    CSTRING(16)
vPStr    PSTRING(16)
vDate    DATE
vTime    TIME
vGroup   GROUP
gA         LONG
gB         STRING(8)
         END
vArray   LONG,DIM(4)

  CODE
  vByte = 1
  vShort = -2
  vUShort = 3
  vLong = 100
  vULong = 200
  vSReal = 1.5
  vReal = 3.14159
  vDec = 12.34
  vPDec = 5.678
  vStr = 'Hello'
  vCStr = 'World'
  vPStr = 'Pstr'
  vDate = 80000
  vTime = 1234
  vGroup.gA = 7
  vGroup.gB = 'grp'
  vArray[1] = 11
  vArray[2] = 22
  vReal = DoCalc(vLong, vReal)
  HALT

DoCalc PROCEDURE(LONG pA, REAL pB)
locX   LONG
locY   REAL
locStr STRING(10)
  CODE
  locX = pA + 1
  locY = pB * 2
  locStr = 'calc'
  RETURN locY
