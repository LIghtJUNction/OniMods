; Assembly listing for method SymbolOverrideInfoGpuData:SetSymbolOverrideInfo(int,byref):this (FullOpts)
; Emitting BLENDED_CODE for generic X64 + VEX + EVEX on Unix
; FullOpts code
; optimized code
; rbp based frame
; partially interruptible
; No PGO data

G_M000_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     rbx
       push     rax
       lea      rbp, [rsp+0x20]
       mov      r15, rdi
       mov      r14d, esi
       mov      rbx, rdx

G_M000_IG02:                ;; offset=0x0015
       mov      rdi, r15
       call     [SymbolOverrideInfoGpuData:get_symbolOverrideInfos():SymbolOverrideInfoGpuData+SymbolOverrideInfo[]:this]
       cmp      r14d, dword ptr [rax+0x08]
       jae      SHORT G_M000_IG04
       mov      edi, r14d
       imul     rdi, rdi, 48
       lea      rdi, bword ptr [rax+rdi+0x10]
       vxorps   xmm0, xmm0, xmm0
       vcvtsi2ss xmm0, xmm0, dword ptr [rbx]
       vmovss   dword ptr [rdi], xmm0
       mov      dword ptr [rdi+0x04], 0x3F800000
       mov      rax, qword ptr [rbx+0x04]
       mov      qword ptr [rdi+0x10], rax
       mov      rax, qword ptr [rbx+0x0C]
       mov      qword ptr [rdi+0x18], rax
       mov      rax, qword ptr [rbx+0x14]
       mov      qword ptr [rdi+0x20], rax
       mov      rax, qword ptr [rbx+0x1C]
       mov      qword ptr [rdi+0x28], rax
       mov      rdi, r15
       call     [SymbolOverrideInfoGpuData:MarkDirty():this]
       nop

G_M000_IG03:                ;; offset=0x006D
       add      rsp, 8
       pop      rbx
       pop      r14
       pop      r15
       pop      rbp
       ret

G_M000_IG04:                ;; offset=0x0078
       call     CORINFO_HELP_RNGCHKFAIL
       int3

; Total bytes of code 126

; Assembly listing for method MonoMod.Utils.DynamicMethodDefinition:SymbolOverrideInfoGpuData.SetSymbolOverrideInfo_Patch1(SymbolOverrideInfoGpuData,int,byref) (FullOpts)
; Emitting BLENDED_CODE for generic X64 + VEX + EVEX on Unix
; FullOpts code
; optimized code
; rbp based frame
; fully interruptible
; No PGO data
; 1 inlinees with PGO data; 2 single block inlinees; 2 inlinees without PGO data

G_M000_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     rbx
       sub      rsp, 32
       lea      rbp, [rsp+0x30]
       xor      eax, eax
       mov      qword ptr [rbp-0x20], rax
       mov      gword ptr [rbp-0x28], rdi
       mov      r15d, esi
       mov      rbx, rdx

G_M000_IG02:                ;; offset=0x001D
       xor      eax, eax
       mov      dword ptr [rbp-0x14], eax

G_M000_IG03:                ;; offset=0x0022
       call     [SymbolOverrideInfoGpuData:get_symbolOverrideInfos():SymbolOverrideInfoGpuData+SymbolOverrideInfo[]:this]
       cmp      r15d, dword ptr [rax+0x08]
       jae      G_M000_IG09
       mov      edx, r15d
       imul     rdx, rdx, 48
       lea      rdx, bword ptr [rax+rdx+0x10]
       vxorps   xmm0, xmm0, xmm0
       vcvtsi2ss xmm0, xmm0, dword ptr [rbx]
       vmovd    edi, xmm0
       cmp      dword ptr [rdx], edi
       jne      SHORT G_M000_IG04
       cmp      dword ptr [rdx+0x04], 0x3F800000
       jne      SHORT G_M000_IG04
       mov      rdi, qword ptr [rdx+0x10]
       xor      rdi, qword ptr [rbx+0x04]
       mov      rsi, qword ptr [rdx+0x18]
       xor      rsi, qword ptr [rbx+0x0C]
       or       rdi, rsi
       mov      rsi, qword ptr [rdx+0x20]
       xor      rsi, qword ptr [rbx+0x14]
       or       rdi, rsi
       mov      rsi, qword ptr [rdx+0x28]
       xor      rsi, qword ptr [rbx+0x1C]
       or       rdi, rsi
       je       SHORT G_M000_IG07

G_M000_IG04:                ;; offset=0x0082
       vmovss   dword ptr [rdx], xmm0
       mov      dword ptr [rdx+0x04], 0x3F800000
       mov      rdi, qword ptr [rbx+0x04]
       mov      qword ptr [rdx+0x10], rdi
       mov      rdi, qword ptr [rbx+0x0C]
       mov      qword ptr [rdx+0x18], rdi
       mov      rdi, qword ptr [rbx+0x14]
       mov      qword ptr [rdx+0x20], rdi
       mov      rdi, qword ptr [rbx+0x1C]
       mov      qword ptr [rdx+0x28], rdi

G_M000_IG05:                ;; offset=0x00AD
       mov      rdi, gword ptr [rbp-0x28]
       call     [SymbolOverrideInfoGpuData:MarkDirty():this]

G_M000_IG06:                ;; offset=0x00B7
       mov      dword ptr [rbp-0x14], 1
       jmp      SHORT G_M000_IG10

G_M000_IG07:                ;; offset=0x00C0
       cmp      byte  ptr [(reloc 0x7fd19a05b2c8)], 0
       je       SHORT G_M000_IG05
       cmp      byte  ptr [(reloc 0x7fd19a05b2c9)], 0
       je       SHORT G_M000_IG06
       mov      rdx, 0x7FCA0C002DA8
       mov      rdx, gword ptr [rdx]
       cmp      gword ptr [rbp-0x28], 0
       je       SHORT G_M000_IG08
       mov      rsi, gword ptr [rdx+0x10]
       cmp      byte  ptr [rsi], sil
       lea      rdx, [rbp-0x20]
       mov      rdi, rsi
       mov      rsi, gword ptr [rbp-0x28]
       call     [System.Runtime.CompilerServices.ConditionalWeakTable`2+Container[System.__Canon,System.__Canon]:FindEntry(System.__Canon,byref):int:this]
       cmp      eax, -1
       setne    dil
       movzx    rdi, dil
       xor      rax, rax
       mov      gword ptr [rbp-0x20], rax
       test     edi, edi
       je       SHORT G_M000_IG06
       jmp      SHORT G_M000_IG05

G_M000_IG08:                ;; offset=0x0115
       mov      edi, 4
       call     [System.ThrowHelper:ThrowArgumentNullException(int)]
       int3

G_M000_IG09:                ;; offset=0x0121
       call     CORINFO_HELP_RNGCHKFAIL
       int3

G_M000_IG10:                ;; offset=0x0127
       add      rsp, 32
       pop      rbx
       pop      r15
       pop      rbp
       ret

G_M000_IG11:                ;; offset=0x0130
       push     rax

G_M000_IG12:                ;; offset=0x0131
       lea      rax, G_M000_IG17

G_M000_IG13:                ;; offset=0x0138
       add      rsp, 8
       ret

G_M000_IG14:                ;; offset=0x013D
       push     rax

G_M000_IG15:                ;; offset=0x013E
       mov      rsi, rdi
       mov      gword ptr [rbp-0x30], rsi
       cmp      dword ptr [rbp-0x14], 0
       jne      SHORT G_M000_IG17

G_M000_IG16:                ;; offset=0x014B
       mov      rdi, gword ptr [rbp-0x28]
       call     [CycleTrim.Patches.AnimationSymbolOptimizationPatch:Finalizer(SymbolOverrideInfoGpuData,System.Exception)]
       nop

G_M000_IG17:                ;; offset=0x0156
       cmp      gword ptr [rbp-0x30], 0
       je       SHORT G_M000_IG18
       call     CORINFO_HELP_RETHROW
       int3

G_M000_IG18:                ;; offset=0x0163
       lea      rax, G_M000_IG10

G_M000_IG19:                ;; offset=0x016A
       add      rsp, 8
       ret

; Total bytes of code 367

; Assembly listing for method SymbolOverrideInfoGpuData:SetSymbolOverrideInfo(int,int,int,KBatchGroupData,int,int):this (FullOpts)
; Emitting BLENDED_CODE for generic X64 + VEX + EVEX on Unix
; FullOpts code
; optimized code
; rbp based frame
; partially interruptible
; No PGO data
; 1 inlinees with PGO data; 0 single block inlinees; 1 inlinees without PGO data

G_M000_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     r13
       push     r12
       push     rbx
       sub      rsp, 56
       lea      rbp, [rsp+0x60]
       mov      dword ptr [rbp-0x2C], r9d
       mov      rbx, rdi
       mov      r13d, esi
       mov      r15d, edx
       mov      r12d, ecx
       mov      r14, r8

G_M000_IG02:                ;; offset=0x0026
       xor      edx, edx
       mov      dword ptr [rbp-0x30], edx
       test     r15d, r15d
       jle      G_M000_IG05

G_M000_IG03:                ;; offset=0x0034
       mov      rdi, r14
       cmp      dword ptr [rdi], edi
       call     [KBatchGroupData:get_symbolFrameInstances():System.Collections.Generic.List`1[KAnim+Build+SymbolFrameInstance]:this]
       mov      ecx, dword ptr [rbp+0x10]
       lea      edi, [rcx-0x01]
       mov      edx, dword ptr [rbp-0x30]
       cmp      edi, edx
       jg       G_M000_IG07

G_M000_IG04:                ;; offset=0x0050
       mov      esi, dword ptr [rbp-0x2C]
       add      edi, esi
       cmp      edi, dword ptr [rax+0x10]
       jae      G_M000_IG08
       mov      dword ptr [rbp-0x30], edx
       mov      rax, gword ptr [rax+0x08]
       cmp      edi, dword ptr [rax+0x08]
       jae      G_M000_IG09
       lea      rdi, [rdi+8*rdi]
       vmovdqu  ymm0, ymmword ptr [rax+4*rdi+0x10]
       vmovdqu  ymmword ptr [rbp-0x58], ymm0
       mov      r8d, dword ptr [rax+4*rdi+0x30]
       mov      dword ptr [rbp-0x38], r8d
       mov      rdi, rbx
       call     [SymbolOverrideInfoGpuData:get_symbolOverrideInfos():SymbolOverrideInfoGpuData+SymbolOverrideInfo[]:this]
       mov      edi, dword ptr [rbp-0x30]
       lea      ecx, [rdi+r13]
       cmp      ecx, dword ptr [rax+0x08]
       jae      SHORT G_M000_IG09
       imul     rcx, rcx, 48
       lea      rax, bword ptr [rax+rcx+0x10]
       vxorps   xmm0, xmm0, xmm0
       vcvtsi2ss xmm0, xmm0, r12d
       vmovss   dword ptr [rax], xmm0
       mov      dword ptr [rax+0x04], 0x3F800000
       mov      rcx, qword ptr [rbp-0x54]
       mov      qword ptr [rax+0x10], rcx
       mov      rcx, qword ptr [rbp-0x4C]
       mov      qword ptr [rax+0x18], rcx
       mov      rcx, qword ptr [rbp-0x44]
       mov      qword ptr [rax+0x20], rcx
       mov      rcx, qword ptr [rbp-0x3C]
       mov      qword ptr [rax+0x28], rcx
       inc      edi
       cmp      edi, r15d
       mov      dword ptr [rbp-0x30], edi
       jl       G_M000_IG03

G_M000_IG05:                ;; offset=0x00E6
       mov      rdi, rbx
       call     [SymbolOverrideInfoGpuData:MarkDirty():this]
       nop

G_M000_IG06:                ;; offset=0x00F0
       vzeroupper
       add      rsp, 56
       pop      rbx
       pop      r12
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret

G_M000_IG07:                ;; offset=0x0102
       mov      dword ptr [rbp-0x30], edx
       mov      edi, edx
       mov      edx, dword ptr [rbp-0x30]
       jmp      G_M000_IG04

G_M000_IG08:                ;; offset=0x010F
       call     [System.ThrowHelper:ThrowArgumentOutOfRange_IndexMustBeLessException()]
       int3

G_M000_IG09:                ;; offset=0x0116
       call     CORINFO_HELP_RNGCHKFAIL
       int3

; Total bytes of code 284

; Assembly listing for method MonoMod.Utils.DynamicMethodDefinition:SymbolOverrideInfoGpuData.SetSymbolOverrideInfo_Patch1(SymbolOverrideInfoGpuData,int,int,int,KBatchGroupData,int,int) (FullOpts)
; Emitting BLENDED_CODE for generic X64 + VEX + EVEX on Unix
; FullOpts code
; optimized code
; rbp based frame
; fully interruptible
; No PGO data
; 3 inlinees with PGO data; 2 single block inlinees; 4 inlinees without PGO data

G_M000_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     r13
       push     r12
       push     rbx
       sub      rsp, 88
       lea      rbp, [rsp+0x80]
       xor      eax, eax
       mov      qword ptr [rbp-0x40], rax
       mov      gword ptr [rbp-0x70], rdi
       mov      dword ptr [rbp-0x2C], ecx
       mov      r14d, esi
       mov      ebx, edx
       mov      r15, r8
       mov      r13d, r9d
       mov      r12d, dword ptr [rbp+0x10]

G_M000_IG02:                ;; offset=0x0032
       xor      ecx, ecx
       mov      dword ptr [rbp-0x34], ecx

G_M000_IG03:                ;; offset=0x0037
       xor      ecx, ecx
       xor      edx, edx
       mov      dword ptr [rbp-0x30], edx
       cmp      edx, ebx
       mov      esi, ecx
       jge      G_M000_IG07

G_M000_IG04:                ;; offset=0x0048
       mov      rdi, r15
       cmp      dword ptr [rdi], edi
       call     [KBatchGroupData:get_symbolFrameInstances():System.Collections.Generic.List`1[KAnim+Build+SymbolFrameInstance]:this]
       lea      edi, [r12-0x01]
       mov      ecx, dword ptr [rbp-0x30]
       cmp      edi, ecx
       jg       G_M000_IG14

G_M000_IG05:                ;; offset=0x0063
       add      edi, r13d
       cmp      edi, dword ptr [rax+0x10]
       mov      dword ptr [rbp-0x30], ecx
       jae      G_M000_IG16
       mov      rax, gword ptr [rax+0x08]
       cmp      edi, dword ptr [rax+0x08]
       jae      G_M000_IG17
       lea      rdi, [rdi+8*rdi]
       lea      rdi, bword ptr [rax+4*rdi+0x10]
       mov      rax, qword ptr [rdi+0x04]
       mov      qword ptr [rbp-0x48], rax
       mov      rdx, qword ptr [rdi+0x0C]
       mov      qword ptr [rbp-0x50], rdx
       mov      rsi, qword ptr [rdi+0x14]
       mov      qword ptr [rbp-0x58], rsi
       mov      r8, qword ptr [rdi+0x1C]
       mov      qword ptr [rbp-0x60], r8
       mov      rdi, gword ptr [rbp-0x70]
       call     [SymbolOverrideInfoGpuData:get_symbolOverrideInfos():SymbolOverrideInfoGpuData+SymbolOverrideInfo[]:this]
       mov      edi, dword ptr [rbp-0x30]
       lea      ecx, [r14+rdi]
       cmp      ecx, dword ptr [rax+0x08]
       jae      G_M000_IG17
       imul     rcx, rcx, 48
       lea      rax, bword ptr [rax+rcx+0x10]
       mov      ecx, dword ptr [rbp-0x2C]
       vxorps   xmm0, xmm0, xmm0
       vcvtsi2ss xmm0, xmm0, ecx
       vmovss   dword ptr [rbp-0x64], xmm0
       vmovaps  xmm1, xmm0
       vmovd    edx, xmm1
       cmp      dword ptr [rax], edx
       jne      G_M000_IG10
       cmp      dword ptr [rax+0x04], 0x3F800000
       jne      G_M000_IG10
       mov      rdx, qword ptr [rbp-0x48]
       mov      rsi, rdx
       xor      rsi, qword ptr [rax+0x10]
       mov      r8, qword ptr [rbp-0x50]
       mov      r9, r8
       xor      r9, qword ptr [rax+0x18]
       or       rsi, r9
       mov      r9, qword ptr [rbp-0x58]
       mov      r10, r9
       xor      r10, qword ptr [rax+0x20]
       or       rsi, r10
       mov      r10, qword ptr [rbp-0x60]
       mov      r11, r10
       xor      r11, qword ptr [rax+0x28]
       or       rsi, r11
       mov      qword ptr [rbp-0x48], rdx
       mov      qword ptr [rbp-0x50], r8

G_M000_IG06:                ;; offset=0x0135
       mov      qword ptr [rbp-0x58], r9
       mov      qword ptr [rbp-0x60], r10
       jne      G_M000_IG10
       mov      esi, 1
       inc      edi
       cmp      edi, ebx
       mov      dword ptr [rbp-0x30], edi
       mov      dword ptr [rbp-0x2C], ecx
       jl       G_M000_IG04

G_M000_IG07:                ;; offset=0x0158
       cmp      byte  ptr [(reloc 0x7fd19a05b2c8)], 0
       je       SHORT G_M000_IG08
       cmp      esi, 1
       jne      SHORT G_M000_IG08
       cmp      byte  ptr [(reloc 0x7fd19a05b2c9)], 0
       je       SHORT G_M000_IG09
       mov      rdx, 0x7FCA0C002DA8
       mov      rdx, gword ptr [rdx]
       cmp      gword ptr [rbp-0x70], 0
       je       G_M000_IG15
       mov      rdi, gword ptr [rdx+0x10]
       cmp      byte  ptr [rdi], dil
       lea      rdx, [rbp-0x40]
       mov      rsi, gword ptr [rbp-0x70]
       call     [System.Runtime.CompilerServices.ConditionalWeakTable`2+Container[System.__Canon,System.__Canon]:FindEntry(System.__Canon,byref):int:this]
       cmp      eax, -1
       setne    dil
       movzx    rdi, dil
       xor      rax, rax
       mov      gword ptr [rbp-0x40], rax
       test     edi, edi
       je       SHORT G_M000_IG09

G_M000_IG08:                ;; offset=0x01B1
       mov      rdi, gword ptr [rbp-0x70]
       call     [SymbolOverrideInfoGpuData:MarkDirty():this]

G_M000_IG09:                ;; offset=0x01BB
       mov      dword ptr [rbp-0x34], 1
       jmp      G_M000_IG18

G_M000_IG10:                ;; offset=0x01C7
       mov      rcx, rax
       jmp      SHORT G_M000_IG12

G_M000_IG11:                ;; offset=0x01CC
       add      edi, r13d
       cmp      edi, dword ptr [rax+0x10]
       mov      dword ptr [rbp-0x30], ecx
       jae      G_M000_IG16
       mov      rax, gword ptr [rax+0x08]
       cmp      edi, dword ptr [rax+0x08]
       jae      G_M000_IG17
       lea      rdi, [rdi+8*rdi]
       lea      rdi, bword ptr [rax+4*rdi+0x10]
       mov      rdx, qword ptr [rdi+0x04]
       mov      qword ptr [rbp-0x48], rdx
       mov      r8, qword ptr [rdi+0x0C]
       mov      qword ptr [rbp-0x50], r8
       mov      r9, qword ptr [rdi+0x14]
       mov      qword ptr [rbp-0x58], r9
       mov      r10, qword ptr [rdi+0x1C]
       mov      qword ptr [rbp-0x60], r10
       mov      rdi, gword ptr [rbp-0x70]
       call     [SymbolOverrideInfoGpuData:get_symbolOverrideInfos():SymbolOverrideInfoGpuData+SymbolOverrideInfo[]:this]
       mov      ecx, dword ptr [rbp-0x30]
       lea      edx, [r14+rcx]
       cmp      edx, dword ptr [rax+0x08]
       jae      G_M000_IG17
       imul     rdx, rdx, 48
       lea      rax, bword ptr [rax+rdx+0x10]
       mov      rdx, rax
       vmovss   xmm0, dword ptr [rbp-0x64]
       vmovaps  xmm1, xmm0
       mov      edi, ecx
       mov      rcx, rdx

G_M000_IG12:                ;; offset=0x0245
       vmovss   dword ptr [rcx], xmm1
       mov      dword ptr [rax+0x04], 0x3F800000
       mov      rdx, qword ptr [rbp-0x48]
       mov      qword ptr [rax+0x10], rdx
       mov      r8, qword ptr [rbp-0x50]
       mov      qword ptr [rax+0x18], r8
       mov      r9, qword ptr [rbp-0x58]
       mov      qword ptr [rax+0x20], r9
       mov      r10, qword ptr [rbp-0x60]
       mov      qword ptr [rax+0x28], r10
       inc      edi
       mov      dword ptr [rbp-0x30], edi
       cmp      edi, ebx
       jge      G_M000_IG08
       mov      rdi, r15
       call     [KBatchGroupData:get_symbolFrameInstances():System.Collections.Generic.List`1[KAnim+Build+SymbolFrameInstance]:this]
       lea      edi, [r12-0x01]
       mov      ecx, dword ptr [rbp-0x30]
       cmp      edi, ecx
       jle      G_M000_IG11

G_M000_IG13:                ;; offset=0x0296
       mov      dword ptr [rbp-0x30], ecx
       mov      edi, ecx
       mov      ecx, dword ptr [rbp-0x30]
       jmp      G_M000_IG11

G_M000_IG14:                ;; offset=0x02A3
       mov      dword ptr [rbp-0x30], ecx
       mov      edi, ecx
       mov      ecx, dword ptr [rbp-0x30]
       jmp      G_M000_IG05

G_M000_IG15:                ;; offset=0x02B0
       mov      edi, 4
       call     [System.ThrowHelper:ThrowArgumentNullException(int)]
       int3

G_M000_IG16:                ;; offset=0x02BC
       call     [System.ThrowHelper:ThrowArgumentOutOfRange_IndexMustBeLessException()]
       int3

G_M000_IG17:                ;; offset=0x02C3
       call     CORINFO_HELP_RNGCHKFAIL
       int3

G_M000_IG18:                ;; offset=0x02C9
       add      rsp, 88
       pop      rbx
       pop      r12
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret

G_M000_IG19:                ;; offset=0x02D8
       push     rax

G_M000_IG20:                ;; offset=0x02D9
       lea      rax, G_M000_IG25

G_M000_IG21:                ;; offset=0x02E0
       add      rsp, 8
       ret

G_M000_IG22:                ;; offset=0x02E5
       push     rax

G_M000_IG23:                ;; offset=0x02E6
       mov      rsi, rdi
       mov      gword ptr [rbp-0x78], rsi
       cmp      dword ptr [rbp-0x34], 0
       jne      SHORT G_M000_IG25

G_M000_IG24:                ;; offset=0x02F3
       mov      rdi, gword ptr [rbp-0x70]
       call     [CycleTrim.Patches.AnimationSymbolOptimizationPatch:Finalizer(SymbolOverrideInfoGpuData,System.Exception)]
       nop

G_M000_IG25:                ;; offset=0x02FE
       cmp      gword ptr [rbp-0x78], 0
       je       SHORT G_M000_IG26
       call     CORINFO_HELP_RETHROW
       int3

G_M000_IG26:                ;; offset=0x030B
       lea      rax, G_M000_IG18

G_M000_IG27:                ;; offset=0x0312
       add      rsp, 8
       ret

; Total bytes of code 791
