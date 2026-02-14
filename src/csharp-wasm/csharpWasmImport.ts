import { dotnet } from "./build/wwwroot/_framework/dotnet";

const initializationPromise = new Promise<CsharpWasm>(async (resolve, reject) => {
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

export default initializationPromise;

export enum Direction {
    Down = -1,
    Flat = 0,
    Up = 1,
}

export type CsharpWasm = {
    Program: {
        HelloWorld(solverParamsJson: string, progressCallback: (progress: number) => void): Promise<string>;
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

export const GenerateLayersHeightMap = async (directions: Direction[][], progressCallback: (progress: number) => void) => {
    const solverParamsJson = JSON.stringify({
        Directions: directions,
    });
    const csharpWasm = await initializationPromise;
    const resultJson = await csharpWasm.Program.HelloWorld(solverParamsJson, progressCallback);
    const result = JSON.parse(resultJson) as {
        HeightMap: number[][],
    };
    return result.HeightMap;
}