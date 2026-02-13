import { dotnet } from "./build/wwwroot/_framework/dotnet";

export default new Promise<CsharpWasm>(async (resolve, reject) => {
    const { getAssemblyExports, getConfig } = await dotnet
        .withDiagnosticTracing(false)
        .create();

    const config = getConfig();
    const exports = await getAssemblyExports(config.mainAssemblyName);
    const csharpWasm = exports.CsharpWasm;

    CsharpWasmLoading = {
        loaded: true,
        wasm: csharpWasm,
    };

    resolve(csharpWasm);
});

export type CsharpWasm = {
    Program: {
        HelloWorld(progressCallback: (progress: number) => void): Promise<void>;
    }
}

export let CsharpWasmLoading: {
    loaded: false,
    wasm: undefined,
} | {
    loaded: true,
    wasm: CsharpWasm,
} = {
    loaded: false,
    wasm: undefined,
};