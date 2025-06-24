; --------------------------------------------------------------------
;  updateEffects - 4 µs IRQ for SID-Voice + Digi-Drum
; --------------------------------------------------------------------

#include <avr/io.h>

; ---------------- constants -----------------------------------------
        .set  SID_SIZE, 6
        .set  DD_SIZE, 7
        .set  CHIP_STRIDE, SID_SIZE*3     ; 18
        .set  DD_STRIDE, DD_SIZE*3        ; 21

        .set  REG_A_LEVEL, 8
        .set  TICK_MASK, 0x0F

        .set  __SREG__, 0x3F

        .set PORTD_IO, 0x0B      ; PORTD 0x2B → 0x0B
        .set PORTB_IO, 0x05      ; PORTB 0x25 → 0x05
        .set PORTF_IO, 0x2F      ; PORTF 0x31 → 0x11

        .set DDRD_IO, 0x0A

        .set SELMASK, ((1<<PF4) | (1<<PF6) | (1<<PF7))

; ---------------- external objects coming from the C++ --------------
        .extern  sid
        .extern  dd
        .extern _dSampleAddr                    ; => (&sampleAddress[0]) in FLASH
        .extern _dSampleLen                     ; => (&sampleLen[0])     in FLASH
        .extern  _ZN11YM2149Class11currentChipE ; uint8_t

_selectYM:
        push  r18
        push  r19
        in    r19, __SREG__
        cli

        ldi   r18, 2
        sub   r18,  r24          ; r24 = chip -> r18 = 2-chip

        in    r24,  PORTF
        andi  r24, ~SELMASK
        sbrc  r18, 0             ; bit-0 ? => PF4
        ori   r24, (1<<PF4)
        sbrc  r18, 1             ; bit-1 ? => PF6
        ori   r24, (1<<PF6)
        sbrc  r18, 2             ; bit-2 ? => PF7
        ori   r24, (1<<PF7)
        out   PORTF, r24

        out   __SREG__, r19
        pop   r19
        pop   r18
        ret

; ---------------- tiny helper to write YM ---------------------------
;  r24 = register number (already AND-masked 0-31)
;  r25 = data byte
;
_ymWrite:
        ; --- address phase ------------------------------------------
        sts     PORTD,  r24        ; bus D0-D7
        lds     r16,   PORTB
        ori     r16,   (1<<PB6)    ; BC1 ↑
        sts     PORTB, r16
        lds     r16,   PORTF
        ori     r16,   (1<<PF5)    ; BDIR↑
        sts     PORTF, r16
        nop
        nop
        nop
        nop                         ; ≥ 250 ns latch
        andi    r16,  ~(1<<PF5)     ; BDIR ↓
        sts     PORTF, r16
        lds     r16,   PORTB
        andi    r16,  ~(1<<PB6)     ; BC1 ↓
        sts     PORTB, r16

        ; --- data phase ---------------------------------------------
        sts     PORTD,  r25
        lds     r16,   PORTF
        ori     r16,   (1<<PF5)     ; strobe
        sts     PORTF, r16
        andi    r16,  ~(1<<PF5)
        sts     PORTF, r16
        ret

; --------------------------------------------------------------------
        .text
        .global updateEffects
updateEffects:
; ---------- prologue -------------------------------------------------
        push    r0
        in      r0, __SREG__
        push    r0
        push    r1
        clr     r1

        push    r18
        push    r19
        push    r20
        push    r21
        push    r22
        push    r23
        push    r24
        push    r25
        push    r26
        push    r27
        push    r30
        push    r31

; ----- Z = &sid[0][0]  ,  Y = &dd[0][0] ------------------------------
        ldi     r30, lo8(sid)
        ldi     r31, hi8(sid)
        ldi     r28, lo8(dd)
        ldi     r29, hi8(dd)

        ldi     r17, 0                 ; chip = 0
chip_loop:
; ----- select chip if necessary --------------------------------------
        lds     r24, _ZN11YM2149Class11currentChipE
        cp      r17, r24
        breq    chip_selected
        mov     r24, r17
        rcall   _selectYM
        sts     _ZN11YM2149Class11currentChipE, r17
chip_selected:

        ldi     r16, 0                 ; voice = 0
voice_loop:
; --------------------------------------------------------------------
; SID block
; --------------------------------------------------------------------
        ld      r18, Z+                ; active?
        tst     r18
        breq    sid_skip

        ld      r19, Z+                ; level
        ld      r20, Z+                ; reload L
        ld      r21, Z+                ; reload H
        ld      r22, Z+                ; phase  L
        ld      r23, Z                 ; phase  H   (Z points here)

        subi    r22, 1
        sbci    r23, 0
        st      Z,   r23
        st      -Z,  r22               ; pre-decrement store (Z-1)

        brne    sid_done               ; phase > 0 ?

        ; ---- toggle / reload ----
        st      Z,   r21               ; phase = reload
        st      -Z,  r20

        ldd     r18, Z+1               ; toggle byte
        eor     r18, 1
        std     Z+1, r18

        ; choose level
        tst     r18
        breq    sid_zero
        mov     r25, r19
        rjmp    sid_write
sid_zero:
        clr     r25
sid_write:
        ldi     r26, REG_A_LEVEL
        mov     r24, r16
        add     r24, r26
        rcall   _ymWrite
sid_done:
sid_skip:

; --------------------------------------------------------------------
; Digi-Drum block
; --------------------------------------------------------------------
        movw  r14, r30           ; r15:r14 = old Z  (tmp save)

; X = pgm_read_word( _dSampleAddr + sample*2 )
        ldi   ZL, lo8(_dSampleAddr)
        ldi   ZH, hi8(_dSampleAddr)
        add   ZL, r25            ; r25 = sample #
        adc   ZH, r1
        lpm   XL, Z+             ; low
        lpm   XH, Z              ; high  (only 16-bit ptr in this build)

; byte = pgm_read_byte( X + pos )
        add   XL, r24
        adc   XH, r1
        lpm   r18, Z             ; r18 = sample byte   (Z not used now)
        mov   r26, r18           ; r26 = data

; ---- (2) RESTORE 'Z' FOR THE NEXT LOOP ---------------------------
        movw  r30, r14

; ---- (3) FETCH sampleLen FROM FLASH IN 16 BIT --------------------
        ldi   ZL, lo8(_dSampleLen)
        ldi   ZH, hi8(_dSampleLen)
        add   ZL, r25            ; index
        adc   ZH, r1
        lpm   r27, Z             ; length-low
        ; (high byte not needed - all reduced samples <256)

; -------------- deactivate when (++pos >= lenLow) -----------------
        cp    r24, r27
        brlo    dd_write          ;  ← stays in range now!

        ; -------------- deactivate -----------------------
        subi    r28, 4           ; Y -= 4
        st      Y,   r1          ; clear “active” flag (r1 is always 0)
        adiw    r28, 4           ; Y += 4
        rjmp    dd_done

dd_write:
        andi    r26, TICK_MASK
        mov     r25, r26
        ldi     r26, REG_A_LEVEL
        mov     r24, r16
        add     r24, r26
        rcall   _ymWrite
dd_done:
dd_skip:

; ----- advance pointers ----------------------------------------------
        adiw    r30, SID_SIZE
        adiw    r28, DD_SIZE

        inc   r16
        cpi   r16, 3
        brlt  1f
        rjmp  voice_loop
1:

; ----- next chip ------------------------------------------------------
        adiw    r30, CHIP_STRIDE - (SID_SIZE*3)
        adiw    r28, DD_STRIDE  - (DD_SIZE*3)

        inc   r17
        cpi   r17, 3
        brlt  1f
        rjmp  chip_loop
1:

; ---------- epilogue --------------------------------------------------
        pop     r31
        pop     r30
        pop     r27
        pop     r26
        pop     r25
        pop     r24
        pop     r23
        pop     r22
        pop     r21
        pop     r20
        pop     r19
        pop     r18
        pop     r1
        pop     r0
        out     __SREG__, r0
        pop     r0
        reti
