use lazy_static::lazy_static;
use netcorehost::{hostfxr::AssemblyDelegateLoader, nethost, pdcstr};
use std::env;
use std::ffi::{c_char, CString};

lazy_static! {
    static ref ASM: AssemblyDelegateLoader = {
        let hostfxr = nethost::load_hostfxr().unwrap();

        let exe_path = env::current_exe().expect("Failed to get the executable path");
        let dotnet_dir = exe_path
            .parent()
            .expect("Failed to get the executable directory")
            .join("dotnet");
        
        let runtime_config_path = dotnet_dir.join("TauriDotNetBridge.runtimeconfig.json");
        let dll_path = dotnet_dir.join("TauriDotNetBridge.dll");

        println!("Using TauriDotNetBridge.runtimeconfig.json: {:?}", runtime_config_path);
        println!("Using TauriDotNetBridge.dll: {:?}", dll_path);
    
        let context = hostfxr
            .initialize_for_runtime_config(runtime_config_path.as_path())
            .expect("Invalid runtime configuration");

        context
            .get_delegate_loader_for_assembly(dll_path.as_path())
            .expect("Failed to load DLL")
    };
}

pub fn process_request(request: &str) -> String {
    let instance = &ASM;

    let process_request = instance
        .get_function_with_unmanaged_callers_only::<fn(text_ptr: *const u8, text_length: i32) -> *mut c_char>(
            pdcstr!("TauriDotNetBridge.Bridge, TauriDotNetBridge"),
            pdcstr!("ProcessRequest"),
        )
        .unwrap();

    let response_ptr = process_request(request.as_ptr(), request.len() as i32);

    let response = unsafe { CString::from_raw(response_ptr) };

    format!("{}", response.to_string_lossy())
}
