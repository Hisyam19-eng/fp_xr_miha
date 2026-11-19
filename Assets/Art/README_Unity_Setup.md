# Panduan Integrasi Aset Unity (Unity 6 / URP / Built-in)

Seluruh aset 3D dalam folder `Unity_Assets/` telah dinormalisasi dan dioptimalkan untuk siap pakai (*drop-in ready*) ke proyek **Unity (Unity 6.x / URP / XR)**.

---

## 1. Struktur Folder

```text
Unity_Assets/
├── Characters/
│   └── Miha/
│       ├── miha_animated.fbx          # Model FBX + Armature (12 tulang) + Animasi Baked
│       ├── miha_animated.glb          # Format glTF 2.0 (alternatif untuk WebGL / UnityGLTF)
│       └── miha_animation_preview.mp4 # Video preview siklus gerak 1-250 frame
├── Environment/
│   └── Kamar_Kosan/
│       ├── kamar_kosan_full.fbx       # Seluruh ruangan kamar & perabot (single asset)
│       ├── kamar_kosan_full.glb       # Format glTF 2.0
│       └── Modular/                   # Partisi modular per koleksi
│           ├── room_structure.fbx     # Dinding, lantai, pintu, kusen, plint
│           ├── furniture_bed.fbx      # Dipan, kasur springbed, bantal, selimut
│           ├── furniture_desk.fbx     # Meja belajar & kursi kerja hidrolik
│           ├── furniture_wardrobe.fbx # Lemari pakaian & laci
│           ├── props_decor.fbx        # Jam dinding, buku rak, memo notes, pot sukulen
│           └── tools_obeng.fbx        # Toolkit reparasi hardware, obeng mekanik, mat & bits
└── Props/
    └── Laptop_LOQ15IRX9/
        ├── LOQ15IRX9_closed_display.fbx # Model presisi Lenovo LOQ 15IRX9
        ├── LOQ15IRX9_closed_display.glb # Format glTF 2.0
        └── Textures/
            └── LOQ15IRX9_Albedo_4K.jpg  # Tekstur Albedo 4096x4096 (ekstrak dari scan photogrammetry)
```

---

## 2. Spesifikasi Standar Unit & Sumbu

- **Unit Skala**: `1 unit = 1 meter`. Seluruh objek telah diterapkan (*Apply Scale*) menjadi `(1.0, 1.0, 1.0)`.
- **Orientasi Sumbu**: Koordinat ekspor telah disesuaikan dengan konvensi Unity:
  - **Forward**: `-Z`
  - **Up**: `+Y`
- **Model Import Settings di Unity**:
  - `Scene Units`: centang `Convert Units` (default Unity) atau biarkan `Scale Factor = 1`.
  - `Normals`: `Import` (atau `Calculate` dengan `Smooth Angle = 60°`).

---

## 3. Petunjuk Material & Shader di Unity (URP Lit / Standard)

### A. Laptop Lenovo LOQ 15IRX9
1. Tarik folder `Textures/LOQ15IRX9_Albedo_4K.jpg` ke Unity.
2. Buat Material baru bernama `M_LOQ15IRX9_Albedo` (Shader: `Universal Render Pipeline/Lit`).
3. Pasang `LOQ15IRX9_Albedo_4K.jpg` pada slot **Base Map**.
4. Set **Smoothness**: `0.3 - 0.5`, **Metallic**: `0.0`.
5. Untuk material komponen lain (`LOQ_Luna_Grey_Panel`, `LOQ_Display_Luna_Grey`, `LOQ_Connector_Metal`, dll.), Unity akan otomatis men-generate material warna standar saat opsi `Materials -> Extract Materials` ditekan pada FBX.

### B. Karakter Miha (`Miha_Rig`)
1. Pilih `miha_animated.fbx` di Project View.
2. Di tab **Rig**:
   - Pilih `Animation Type: Generic` (karena rig robot mini non-humanoid).
   - `Root Node`: pilih `root`.
3. Di tab **Animation**:
   - Klip `Miha_Baked_FullSequence` (Frame 1-250) sudah ter-bake penuh mencakup:
     - `Idle_Hover` (Frame 1–96)
     - `Greeting_Wave` (Frame 97–168)
     - `Boost_Dash` (Frame 169–228)
     - `Idle_Hover` loopback (Frame 229–250)
   - Anda dapat membagi klip ini menjadi sub-clips terpisah pada Inspector FBX jika ingin mengontrol transisi via Animator State Machine.
4. **Efek Emisif (Glow)**:
   - Pada material `Miha_Jet_Core`, `Miha_Jet_Cyan`, `Miha_Eye_Cyan`, dan `Miha_Blush_Pink`, aktifkan checkbox **Emission** di shader URP Lit.
   - Atur warna emisi masing-masing (Cyan: `#00E5FF`, Pink: `#FF3399`) dan intensitas HDR `+1.5` hingga `+3.0` untuk mendapatkan pendaran Bloom di kamera URP Post-Processing.

### C. Lingkungan Kamar Kosan
- Semua material menggunakan model warna fisik (PBR standard colors: kayu oak, kain, plastik, keramik, dll.).
- Saat drag & drop `kamar_kosan_full.fbx` atau partisi modular ke dalam Scene, klik Inspector -> tab `Materials` -> klik **Extract Materials** untuk menyimpan aset Material `.mat` ke folder `Materials/`.

---

## 4. Penggunaan dalam Mixed Reality / Quest 3 (XR-FixIt)
- Letakkan `room_structure.fbx` atau `furniture_desk.fbx` sebagai referensi spatial alignment meja kerja.
- Tempatkan `LOQ15IRX9_closed_display.fbx` dan `tools_obeng.fbx` di atas meja (`Meja_Top`).
- Karakter `Miha` dapat di-spawn di koordinat melayang ~15-25 cm di atas bodi laptop sebagai NPC pemandu langkah teknisi.
