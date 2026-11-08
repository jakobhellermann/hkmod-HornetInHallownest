fn main() -> Result<(), Box<dyn std::error::Error>> {
    csbindgen::Builder::default()
        .input_extern_file("src/ffi.rs")
        .csharp_dll_name("bundle_transpile")
        .generate_csharp_file("../../UnityCompat/NativeMethods.g.cs")
        .unwrap();

    Ok(())
}
