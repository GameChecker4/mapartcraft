import CsharpWasmImport, { CsharpWasm } from "../../../csharp-wasm/csharpWasmImport";

// begin variables passed in onmessage
var coloursJSON: Record<string, {
  tonesRGB: Record<ColourTone, number[]>,
  mapdatId: number
  blocks: Record<string, {
    validVersions: Record<string, string | {
      NBTName: string,
      NBTArgs: Record<string, string>,
    }>,
    supportBlockMandatory: boolean,
  }>,
}>

type StaircaseMode = "OFF" | "CLASSIC" | "VALLEY" | "LAYERED" | "FULL_DARK" | "FULL_LIGHT";

var MapModes: {
  SCHEMATIC_NBT: {
    staircaseModes: Record<StaircaseMode, {
      uniqueId: number,
    }>,
  };
};

type SupportBlocksMode = "NONE" | "IMPORTANT" | "ALL_OPTIMIZED" | "ALL_DOUBLE_OPTIMIZED";

var WhereSupportBlocksModes: Record<SupportBlocksMode, {
  uniqueId: number,
}>;

var optionValue_version: {
  MCVersion: string;
  NBTVersion: number;
};

var optionValue_staircasing: number;
var optionValue_whereSupportBlocks: number;
var optionValue_supportBlock: string;
var pixelsData: number[];
var maps: MapData[][];
var currentSelectedBlocks: Record<string, string>;
// end onmessage variables

var exactColourCache = new Map<number, Colour>(); // for mapping RGB that exactly matches in coloursJSON to colourSetId and tone

var progressReportHead: string;

var csharpWasm: CsharpWasm;

enum Direction {
  Down = -1,
  Flat = 0,
  Up = 1,
}

/*
  A mapping from type names to NBT type numbers.
  This is NOT just an enum, these values have to stay as they are
  https://minecraft.wiki/w/NBT_format#TAG_definition
*/
enum TagType {
  end = 0,
  byte = 1,
  short = 2,
  int = 3,
  long = 4,
  float = 5,
  double = 6,
  byteArray = 7,
  string = 8,
  list = 9,
  compound = 10,
  intArray = 11,
  longArray = 12,
};

class NBTWriter {
  buffer: ArrayBuffer;
  dataView: DataView<ArrayBuffer>;
  arrayView: Uint8Array<ArrayBuffer>;
  offset: number;
  constructor() {
    if (typeof ArrayBuffer === "undefined") {
      throw new Error("Missing required type ArrayBuffer");
    }
    if (typeof DataView === "undefined") {
      throw new Error("Missing required type DataView");
    }
    if (typeof Uint8Array === "undefined") {
      throw new Error("Missing required type Uint8Array");
    }
    /* Will be auto-resized (x2) on write if necessary. */
    this.buffer = new ArrayBuffer(1024);

    /* These are recreated when the buffer is */
    this.dataView = new DataView(this.buffer);
    this.arrayView = new Uint8Array(this.buffer);
    this.offset = 0;
  }

  encodeUTF8(str: string) {
    let array: number[] = []
    let i: number, c: number;
    for (i = 0; i < str.length; i++) {
      c = str.charCodeAt(i);
      if (c === 0x0) {
        array.push(0xc0);
        array.push(0x80);
      } else if (c < 0x80) {
        array.push(c);
      } else if (c < 0x800) {
        array.push(0xc0 | (c >> 6));
        array.push(0x80 | (c & 0x3f));
      } else if (c < 0x10000) {
        array.push(0xe0 | (c >> 12));
        array.push(0x80 | ((c >> 6) & 0x3f));
        array.push(0x80 | (c & 0x3f));
      } else {
        // unsure if this is accurate, however we never need such exotic unicode characters
        array.push(0xf0 | ((c >> 18) & 0x07));
        array.push(0x80 | ((c >> 12) & 0x3f));
        array.push(0x80 | ((c >> 6) & 0x3f));
        array.push(0x80 | (c & 0x3f));
      }
    }
    return array;
  }

  accommodate(size: number) {
    // Ensures that the buffer is large enough to write `size` bytes at the current `this.offset`.
    let requiredLength = this.offset + size;
    if (this.buffer.byteLength >= requiredLength) {
      return;
    }

    let newLength = this.buffer.byteLength;
    while (newLength < requiredLength) {
      newLength *= 2;
    }
    let newBuffer = new ArrayBuffer(newLength);
    let newArrayView = new Uint8Array(newBuffer);
    newArrayView.set(this.arrayView);

    // If there's a gap between the end of the old buffer
    // and the start of the new one, we need to zero it out
    if (this.offset > this.buffer.byteLength) {
      newArrayView.fill(0, this.buffer.byteLength, this.offset);
    }

    this.buffer = newBuffer;
    this.dataView = new DataView(newBuffer);
    this.arrayView = newArrayView;
  }

  write(dataType: string, size: number, value: number) {
    this.accommodate(size);
    this.dataView[`set${dataType}`](this.offset, value);
    this.offset += size;
  }

  writeByType({ type, value }: NBTRecord) {
    switch (type) {
      case TagType.end: {
        this.writeByType({ type: TagType.byte, value: 0 });
        break;
      }
      case TagType.byte: {
        this.write("Int8", 1, value);
        break;
      }
      case TagType.short: {
        this.write("Int16", 2, value);
        break;
      }
      case TagType.int: {
        this.write("Int32", 4, value);
        break;
      }
      case TagType.long: {
        // NB: special: JS doesn't support native 64 bit ints; pass an array of two 32 bit ints to this case
        this.write("Int32", 4, value[0]);
        this.write("Int32", 4, value[1]);
        break;
      }
      case TagType.float: {
        this.write("Float32", 4, value);
        break;
      }
      case TagType.double: {
        this.write("Float64", 8, value);
        break;
      }
      case TagType.byteArray: {
        this.writeByType({ type: TagType.int, value: value.length });
        this.accommodate(value.length);
        this.arrayView.set(value, this.offset);
        this.offset += value.length;
        break;
      }
      case TagType.string: {
        let bytes = this.encodeUTF8(value);
        this.writeByType({ type: TagType.short, value: bytes.length });
        this.accommodate(bytes.length);
        this.arrayView.set(bytes, this.offset);
        this.offset += bytes.length;
        break;
      }
      case TagType.list: {
        // Pass a dicitonary {"type": TagTypes.blah, "value": [] }
        this.writeByType({ type: TagType.byte, value: value.type });
        this.writeByType({ type: TagType.int, value: value.value.length });
        for (let i = 0; i < value.value.length; i++) {
          this.writeByType({ type: value.type, value: value.value[i] } as NBTRecord);
        }
        break;
      }
      case TagType.compound: {
        // This is the rich tagtype we will interact with a lot
        // Pass a dictionary {"type": TagTypes.blah, "value": ... }
        // {
        //   author: { type: TagTypes.string, value: "Steve" },
        //   stuff: {
        //       type: TagTypes.compound,
        //       value: {
        //         foo: { type: int, value: 42 },
        //         bar: { type: string, value: 'Hi!' }
        //       }
        //   }
        // }
        Object.keys(value).forEach((key) => {
          this.writeByType({ type: TagType.byte, value: value[key].type });
          this.writeByType({ type: TagType.string, value: key });
          this.writeByType({ type: value[key].type, value: value[key].value } as NBTRecord); // this is where the nice recursion happens
        });
        this.writeByType({ type: TagType.end, value: 0 });
        break;
      }
      case TagType.intArray: {
        this.writeByType({ type: TagType.int, value: value.length });
        for (let i = 0; i < value.length; i++) {
          this.writeByType({ type: TagType.int, value: value[i] });
          // https://lkml.org/lkml/2012/7/6/495
        }
        break;
      }
      case TagType.longArray: {
        throw new Error("LongArray NBT not implemented");
      }
      default: {
        throw new Error(`Unknown data type ${type} for value ${value}`);
      }
    }
  }

  writeTopLevelCompound(value: { name: string; value: any; }) {
    // For writing a top level JSON object as a compound tag.
    // This is of the form {"name": "blah", "value": {...}}
    // This is not just this.writeByType(TagTypes.compound, value); we add the appropriate compound prefix etc
    this.writeByType({ type: TagType.byte, value: TagType.compound });
    this.writeByType({ type: TagType.string, value: value.name });
    this.writeByType({ type: TagType.compound, value: value.value });
  }

  getData() {
    /*
      Returns the writen data as a slice from the internal buffer, cutting off any padding at the end.
    */
    this.accommodate(0); /* make sure the offset is inside the buffer */
    return this.buffer.slice(0, this.offset);
  }
}

type NBTCompoundValue = Record<string, NBTRecord>;

type NBTRecord = {
  type: TagType.end | TagType.byte | TagType.short | TagType.int | TagType.float | TagType.double;
  value: number,
} | {
  type: TagType.string,
  value: string,
} | {
  type: TagType.long | TagType.byteArray | TagType.intArray | TagType.longArray,
  value: number[],
} | {
  type: TagType.compound,
  value: NBTCompoundValue,
} | {
  type: TagType.list,
  value: NBTListRecord,
};

type NBT<Type extends TagType> = {
  type: Type,
} & NBTRecord;

type NBTCompound<ValueType extends NBTCompoundValue> = {
  type: TagType.compound,
  value: ValueType,
} & NBTRecord;

type NBTCompoundGeneric<ValueType extends NBTRecord> = NBTCompound<Record<string, ValueType>>;

type NBTList<ValueType extends NBTListRecord> = {
  type: TagType.list,
  value: ValueType,
} & NBTRecord;

type NBTListRecord = {
  type: TagType.end | TagType.byte | TagType.short | TagType.int | TagType.float | TagType.double;
  value: number[],
} | {
  type: TagType.string,
  value: string[],
} | {
  type: TagType.long | TagType.byteArray | TagType.intArray | TagType.longArray,
  value: number[][],
} | {
  type: TagType.compound,
  value: NBTCompoundValue[],
} | {
  type: TagType.list,
  value: NBTListRecord[],
};

type NBTs<Type extends TagType> = {
  type: Type,
} & NBTListRecord;

type NBTCompounds<ValueType extends NBTCompoundValue> = {
  type: TagType.compound,
  value: ValueType[],
} & NBTListRecord;

type NBTCompoundsGeneric<ValueType extends NBTRecord> = NBTCompounds<Record<string, ValueType>>;

type NBTLists<ValueType extends NBTListRecord> = {
  type: TagType.list,
  value: ValueType[],
} & NBTListRecord;


type NBTTopLevelCompound<ValueType extends NBTCompoundValue> = {
  name: string,
  value: ValueType,
};

type NBTPhisicalBlock = {
  pos: NBTList<NBTs<TagType.int>>,
  state: NBT<TagType.int>,
};

type NBTPalleteItem = {
  Name: NBT<TagType.string>;
  Properties: NBTCompoundGeneric<NBT<TagType.string>>;
};

type ColourTone = "dark" | "normal" | "light" | "unobtainable";

type Colour = {
  tone: ColourTone,
  colourSetId: string,
};

type MapData = {
  coloursLayout: Colour[][];
  materials: Record<string, number>;
};

type NBTMap = {
  blocks: NBTList<NBTCompounds<NBTPhisicalBlock>>;
  entities: NBTList<NBTCompounds<{}>>;
  palette: NBTList<NBTCompounds<NBTPalleteItem>>;
  size: NBTList<NBTs<TagType.int>>;
  author: NBT<TagType.string>;
  DataVersion: NBT<TagType.int>;
};

class Map_NBT {
  private mapColoursLayout: Colour[][];
  private mapMaterialsCounts: Record<string, number>;
  private NBT_json: NBTTopLevelCompound<NBTMap>;
  private palette_colourSetId_paletteId: Record<string, number>;
  private palette_paletteId_colourSetId: string[];
  private heightMap: number[][];

  constructor(map: MapData) {
    this.mapColoursLayout = map.coloursLayout;
    this.mapMaterialsCounts = map.materials;
    this.NBT_json = {
      name: "",
      value: {
        blocks: {
          type: TagType.list,
          value: {
            type: TagType.compound,
            value: [],
          },
        },
        entities: {
          type: TagType.list,
          value: {
            type: TagType.compound,
            value: [],
          },
        },
        palette: {
          type: TagType.list,
          value: {
            type: TagType.compound,
            value: [],
          },
        },
        size: {
          type: TagType.list,
          value: {
            type: TagType.int,
            value: [], // X, Y, Z
          },
        },
        author: {
          type: TagType.string,
          value: "rebane2001.com/mapartcraft",
        },
        DataVersion: {
          type: TagType.int,
          value: 0,
        },
      },
    };
    this.palette_colourSetId_paletteId = {}; // map coloursJSON colourSetIds to index of corresponding block in palette list
    this.palette_paletteId_colourSetId = []; // map paletteIds (index of an item in this list) to colourSetIds
    this.heightMap = [];
  }

  constructPaletteLookups() {
    // filter $materials to colourSetIds with non-zero materials count
    const nonZeroMaterials = Object.fromEntries(Object.entries(this.mapMaterialsCounts).filter(([_, value]) => value !== 0));
    // now construct palette lookups for non-zero colourSetIds
    Object.keys(nonZeroMaterials).forEach((colourSetId) => {
      this.palette_colourSetId_paletteId[colourSetId] = this.palette_paletteId_colourSetId.length;
      this.palette_paletteId_colourSetId.push(colourSetId);
    });
    // finally add noobline/scaffold material at the end, special key
    this.palette_colourSetId_paletteId["NOOBLINE_SCAFFOLD"] = this.palette_paletteId_colourSetId.length;
    this.palette_paletteId_colourSetId.push("NOOBLINE_SCAFFOLD");
  }

  setNBT_json_palette() {
    this.palette_paletteId_colourSetId.forEach((colourSetId) => {
      let paletteItemToPush = {} as NBTPalleteItem;
      if (colourSetId === "NOOBLINE_SCAFFOLD") {
        paletteItemToPush.Name = {
          type: TagType.string,
          value: `minecraft:${optionValue_supportBlock.toLowerCase()}`,
        };
        // we expect the support block to be something non-exotic with no properties eg netherrack
      } else {
        let blockNBTData = coloursJSON[colourSetId].blocks[currentSelectedBlocks[colourSetId]].validVersions[optionValue_version.MCVersion];
        if (typeof blockNBTData === "string") {
          // this is of the form eg "&1.12.2"
          blockNBTData = coloursJSON[colourSetId].blocks[currentSelectedBlocks[colourSetId]].validVersions[blockNBTData.slice(1)];
          if (typeof blockNBTData === "string")
            throw new Error("Invalid version redirect")
        }
        paletteItemToPush.Name = {
          type: TagType.string,
          value: `minecraft:${blockNBTData.NBTName}`,
        };
        if (Object.keys(blockNBTData.NBTArgs).length !== 0) {
          paletteItemToPush.Properties = { type: TagType.compound, value: {} };
          Object.keys(blockNBTData.NBTArgs).forEach((NBTArg_key) => {
            paletteItemToPush.Properties.value[NBTArg_key] = {
              type: TagType.string,
              value: blockNBTData.NBTArgs[NBTArg_key],
            };
          });
        }
      }
      this.NBT_json.value.palette.value.value.push(paletteItemToPush);
    });
  }

  setNBT_json_DataVersion() {
    this.NBT_json.value.DataVersion.value = optionValue_version.NBTVersion;
  }

  returnPhysicalBlock(x: number, y: number, z: number, colourSetId: string): NBTPhisicalBlock {
    return {
      pos: { type: TagType.list, value: { type: TagType.int, value: [x, y, z] } },
      state: { type: TagType.int, value: this.palette_colourSetId_paletteId[colourSetId] },
    };
  }

  async getPhysicalLayout() {
    let supportsMap = this.generateSupportsMap(this.mapColoursLayout);
    reportProgress(0.3);
    this.heightMap = await this.generateHeightMap(this.mapColoursLayout, supportsMap);
    reportProgress(0.7);
    this.writePhysicalLayout(this.mapColoursLayout, this.heightMap, supportsMap);
    reportProgress(1);
  }

  generateSupportsMap(mapColoursLayout: Colour[][]) {
    const width = mapColoursLayout.length;
    const length = mapColoursLayout[0].length;
    const supportsMap = new Array<number[]>(width);

    const addSupportBlock = (x: number, z: number) => {
      supportsMap[x][z]++;
    }

    for (let x = 0; x < width; x++) {
      const mapColoursLayoutColumn = mapColoursLayout[x];
      supportsMap[x] = new Array<number>(length + 1).fill(0);

      for (let z = 0; z < length; z++) {
        const coloursLayoutBlock = mapColoursLayoutColumn[z];

        // read docs/supportBlocks.md to know how this works
        switch (optionValue_whereSupportBlocks) {
          case WhereSupportBlocksModes.NONE.uniqueId: {
            break;
          }
          case WhereSupportBlocksModes.IMPORTANT.uniqueId: {
            if (isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock)) {
              addSupportBlock(x, z + 1);
            }
            break;
          }
          case WhereSupportBlocksModes.ALL_OPTIMIZED.uniqueId: {
            switch (z) {
              case 0: {
                if (
                  coloursLayoutBlock.tone === "dark" ||
                  (coloursLayoutBlock.tone === "normal" && isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock))
                ) {
                  // first under-support block
                  addSupportBlock(x, z);
                }
                if (coloursLayoutBlock.tone === "dark" && isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock)) {
                  // second under-support block
                  addSupportBlock(x, z);
                }
                break;
              }
              case 1: {
                const coloursLayoutBlock_0 = mapColoursLayoutColumn[z - 1];
                if (
                  coloursLayoutBlock_0.tone === "light" ||
                  coloursLayoutBlock.tone === "dark" ||
                  isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock_0) ||
                  (coloursLayoutBlock.tone === "normal" && isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock))
                ) {
                  // first under-support block
                  addSupportBlock(x, z);
                }
                if (coloursLayoutBlock.tone === "dark" && isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock)) {
                  // second under-support block
                  addSupportBlock(x, z);
                }
                break;
              }
              case mapColoursLayoutColumn.length - 1: {
                // falls through
                const coloursLayoutBlock_north = mapColoursLayoutColumn[z - 1];
                if (
                  coloursLayoutBlock.tone === "light" ||
                  isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock) ||
                  (coloursLayoutBlock.tone === "normal" && isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock_north))
                ) {
                  // first under-support block
                  addSupportBlock(x, z + 1);
                }
                if (coloursLayoutBlock.tone === "light" && isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock_north)) {
                  // second under-support block
                  addSupportBlock(x, z + 1);
                }
              }
              // eslint-disable-next-line no-fallthrough
              default: {
                const coloursLayoutBlock_north = mapColoursLayoutColumn[z - 2];
                const coloursLayoutBlock_inQuestion = mapColoursLayoutColumn[z - 1];
                if (
                  coloursLayoutBlock_inQuestion.tone === "light" ||
                  coloursLayoutBlock.tone === "dark" ||
                  isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock_inQuestion) ||
                  (coloursLayoutBlock.tone === "normal" && isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock)) ||
                  (coloursLayoutBlock_inQuestion.tone === "normal" && isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock_north))
                ) {
                  // first under-support block
                  addSupportBlock(x, z);
                }
                if (
                  (coloursLayoutBlock.tone === "dark" && isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock)) ||
                  (coloursLayoutBlock_inQuestion.tone === "light" && isSupportBlockMandatoryForColourSetIdAndTone(coloursLayoutBlock_north))
                ) {
                  // second under-support block
                  addSupportBlock(x, z);
                }
                break;
              }
            }
            break;
          }
          case WhereSupportBlocksModes.ALL_DOUBLE_OPTIMIZED.uniqueId: {
            switch (z) {
              case 0: {
                addSupportBlock(x, z);
                if (coloursLayoutBlock.tone === "dark") {
                  addSupportBlock(x, z);
                }
                break;
              }
              case mapColoursLayoutColumn.length - 1: {
                addSupportBlock(x, z + 1);
                if (coloursLayoutBlock.tone === "light") {
                  addSupportBlock(x, z + 1);
                }
                // falls through
              }
              // eslint-disable-next-line no-fallthrough
              default: {
                addSupportBlock(x, z);
                const coloursLayoutBlock_inQuestion = mapColoursLayoutColumn[z - 1];
                if (coloursLayoutBlock_inQuestion.tone === "light" || coloursLayoutBlock.tone === "dark") {
                  addSupportBlock(x, z);
                }
                break;
              }
            }
            break;
          }
          default: {
            throw new Error("Unknown support-blocks option");
          }
        }

      }
    }

    return supportsMap;
  }

  async generateHeightMap(colourMap: Colour[][], supportsMap: number[][]) {
    const width = colourMap.length;
    const length = colourMap[0].length;
    const lengthWithNoobline = length + 1;
    let directionMap: Direction[][];

    let heightMap: number[][] = [];

    switch (optionValue_staircasing) {
      case MapModes.SCHEMATIC_NBT.staircaseModes.OFF.uniqueId:
        for (let x = 0; x < width; x++) {
          let column: number[] = [];
          for (let z = 0; z < lengthWithNoobline; z++)
            column.push(2);
          heightMap.push(column);
        }
        break;

      case MapModes.SCHEMATIC_NBT.staircaseModes.FULL_LIGHT.uniqueId:
        for (let x = 0; x < width; x++) {
          let column: number[] = [];
          for (let z = 0; z < lengthWithNoobline; z++)
            column.push(z + 1);
          heightMap.push(column);
        }
        break;

      case MapModes.SCHEMATIC_NBT.staircaseModes.FULL_DARK.uniqueId:
        for (let x = 0; x < width; x++) {
          let column: number[] = [];
          for (let z = 0; z < lengthWithNoobline; z++)
            column.push(lengthWithNoobline - z);
          heightMap.push(column);
        }
        break;

      case MapModes.SCHEMATIC_NBT.staircaseModes.CLASSIC.uniqueId:
        directionMap = generateDirectionMap(colourMap);
        for (let x = 0; x < width; x++) {
          let column: number[] = [];
          column.push(0);
          for (let z = 0; z < length; z++)
            column.push(column[z] + directionMap[x][z]);
          setColumnMinYToZero(column, supportsMap[x]);
          heightMap.push(column);
        }
        break;

      case MapModes.SCHEMATIC_NBT.staircaseModes.LAYERED.uniqueId:
        await (await CsharpWasmImport).Program.HelloWorld(reportProgress);
      // Fall to Valley

      case MapModes.SCHEMATIC_NBT.staircaseModes.VALLEY.uniqueId:
        directionMap = generateDirectionMap(colourMap);
        for (let x = 0; x < width; x++) {
          let column: number[] = [];
          column.push(supportsMap[x][0]);
          for (let z = 0; z < length; z++) {
            let direction = directionMap[x][z];
            let targetHeight = direction == Direction.Down ? 0 : column[z] + direction;
            column.push(Math.max(targetHeight, supportsMap[x][z + 1]));
          }

          for (let z = length - 1; z >= 0; z--) {
            let direction = directionMap[x][z];
            if (direction != Direction.Up)
              column[z] = Math.max(column[z], column[z + 1] - direction);
          }
          heightMap.push(column);
        }
        break;
    }

    return heightMap;

    function generateDirectionMap(colourMap: Colour[][]) {
      let directionMap: Direction[][] = [];
      for (let x = 0; x < colourMap.length; x++) {
        let column: Direction[] = [];
        for (let z = 0; z < colourMap[x].length; z++)
          column.push(getDirection(x, z));
        directionMap.push(column);
      }
      return directionMap;

      function getDirection(x: number, z: number) {
        switch (colourMap[x][z].tone) {
          case "dark":
            return Direction.Down;
          case "normal":
            return Direction.Flat;
          case "light":
            return Direction.Up;
          default:
            throw new Error("Unknown or unsupported colour tone");
        }
      }
    }

    function setColumnMinYToZero(column: number[], supportsColumn: number[]) {
      let minY = column.reduce((minY, y, z) => Math.min(minY, y - supportsColumn[z]), column[0]);
      for (let z = 0; z < column.length; z++)
        column[z] -= minY;
    }
  }

  writePhysicalLayout(colourMap: Colour[][], heightMap: number[][], supportsMap: number[][]) {
    const blocks = this.NBT_json.value.blocks.value.value;

    const addBlockWithSupports = (x: number, z: number, colourSetId: string) => {
      blocks.push(this.returnPhysicalBlock(x, heightMap[x][z], z, colourSetId));
      for (let support = 0; support < supportsMap[x][z]; support++)
        blocks.push(this.returnPhysicalBlock(x, heightMap[x][z] - support - 1, z, "NOOBLINE_SCAFFOLD"));
    }

    const width = heightMap.length;
    const lengthWithNoobline = heightMap[0].length;
    for (let x = 0; x < width; x++) {
      addBlockWithSupports(x, 0, "NOOBLINE_SCAFFOLD");
      for (let z = 1; z < lengthWithNoobline; z++)
        addBlockWithSupports(x, z, colourMap[x][z - 1].colourSetId);
    }
  }

  getMaxY(blocks: NBTPhisicalBlock[]) {
    return blocks.reduce((maxY, block) => Math.max(maxY, block.pos.value.value[1]), Number.MIN_SAFE_INTEGER);
  }

  async setNBT_json_blocks() {
    await this.getPhysicalLayout();
  }

  setNBT_json_size() {
    this.NBT_json.value.size.value.value = [
      this.mapColoursLayout.length,
      this.getMaxY(this.NBT_json.value.blocks.value.value) + 1,
      this.mapColoursLayout[0].length + 1,
    ];
  }

  async getNBT() {
    this.constructPaletteLookups();
    this.setNBT_json_palette();
    this.setNBT_json_DataVersion();
    await this.setNBT_json_blocks();
    this.setNBT_json_size();

    // console.log(NBT_json);

    let nbtWriter = new NBTWriter();
    nbtWriter.writeTopLevelCompound(this.NBT_json);
    return nbtWriter.getData();
  }
}

type NBTMapDat = {
  DataVersion: NBT<TagType.int>;
  data: NBTCompound<{
    scale: NBT<TagType.byte>;
    dimension: NBT<TagType.byte> | NBT<TagType.string>;
    unlimitedTracking: NBT<TagType.byte>;
    trackingPosition: NBT<TagType.byte>;
    locked: NBT<TagType.byte>;
    height: NBT<TagType.short>;
    width: NBT<TagType.short>;
    xCenter: NBT<TagType.int>;
    zCenter: NBT<TagType.int>;
    colors: NBT<TagType.byteArray>;
  }>;
};

class Map_Mapdat {
  private coloursLayout: Colour[][];
  private NBT_json: NBTTopLevelCompound<NBTMapDat>;

  constructor(map: MapData) {
    this.coloursLayout = map.coloursLayout;

    const dataVersion = optionValue_version.NBTVersion;

    this.NBT_json = {
      name: "",
      value: {
        data: {
          type: TagType.compound,
          value: {
            scale: { type: TagType.byte, value: 0 },
            dimension: dataVersion >= 2566 ?
              { type: TagType.string, value: "minecraft:overworld", } :
              { type: TagType.byte, value: dataVersion > 1343 ? 0 : -128, },
            unlimitedTracking: { type: TagType.byte, value: 0 },
            trackingPosition: { type: TagType.byte, value: 0 },
            locked: { type: TagType.byte, value: 1 },
            height: { type: TagType.short, value: 128 },
            width: { type: TagType.short, value: 128 },
            xCenter: { type: TagType.int, value: 0 },
            zCenter: { type: TagType.int, value: 0 },
            colors: { type: TagType.byteArray, value: new Array(16384) },
          },
        },
        DataVersion: { type: TagType.int, value: dataVersion },
      },
    };
  }

  setNBT_json_colors() {
    for (let x = 0; x < 128; x++) {
      for (let z = 0; z < 128; z++) {
        const pixel = this.coloursLayout[x][z];
        const arrayOffset = z * 128 + x;
        let mapdatId: number; // coloursJSON contains the base mapdatId that is multiplied by 4 and added to by one of 0,1,2,3
        if (pixel.colourSetId === "-1") {
          // special colourSetId for transparent maps
          mapdatId = 1;
        } else {
          switch (pixel.tone) {
            case "dark": {
              mapdatId = 4 * coloursJSON[pixel.colourSetId].mapdatId;
              break;
            }
            case "normal": {
              mapdatId = 4 * coloursJSON[pixel.colourSetId].mapdatId + 1;
              break;
            }
            case "light": {
              mapdatId = 4 * coloursJSON[pixel.colourSetId].mapdatId + 2;
              break;
            }
            case "unobtainable": {
              mapdatId = 4 * coloursJSON[pixel.colourSetId].mapdatId + 3;
              break;
            }
            default: {
              throw new Error("Unknown colour tone");
            }
          }
        }
        this.NBT_json.value.data.value.colors.value[arrayOffset] = mapdatId;
      }
      reportProgress((x + 1) / 128);
    }
  }

  getNBT() {
    this.setNBT_json_colors();

    let nbtWriter = new NBTWriter();
    nbtWriter.writeTopLevelCompound(this.NBT_json);
    return nbtWriter.getData();
  }
}

function setupExactColourCache() {
  // we do not care what staircasing option is selected etc as this does not matter
  // this is for exactly matching colours, whose values are never repeated in coloursJSON
  // also RGB could be easily changed to RGBA if Mojang ever added absolute black #000000 and we need to distinguish it from transparent
  // setupExactColourCache also exists in mapCanvas.jsworker but without the "-1" colourSet for transparent
  for (const [colourSetId, colourSet] of Object.entries(coloursJSON)) {
    for (const [toneKey, toneRGB] of Object.entries(colourSet.tonesRGB)) {
      const RGBBinary = (toneRGB[0] << 16) + (toneRGB[1] << 8) + toneRGB[2];
      exactColourCache.set(RGBBinary, {
        colourSetId: colourSetId,
        tone: toneKey as ColourTone,
      });
    }
  }
  exactColourCache.set(0, {
    // special transparent for mapdat
    colourSetId: "-1",
    tone: "normal",
  });
}

function exactRGBToColourSetIdAndTone(pixelRGB: number[]) {
  const RGBBinary = (pixelRGB[0] << 16) + (pixelRGB[1] << 8) + pixelRGB[2];
  let result = exactColourCache.get(RGBBinary);
  if (result === undefined)
    throw new Error("Color not presented in the cache");
  return result;
}

function isSupportBlockMandatoryForColourSetIdAndTone(colourSetIdAndTone: Colour) {
  return coloursJSON[colourSetIdAndTone.colourSetId].blocks[currentSelectedBlocks[colourSetIdAndTone.colourSetId]].supportBlockMandatory;
}

function mergeMaps() {
  // for when we want one big NBT instead of split 1x1s
  maps.forEach((rowOfMaps) => {
    for (let whichMap_x = 1; whichMap_x < rowOfMaps.length; whichMap_x++) {
      rowOfMaps[0].coloursLayout = rowOfMaps[0].coloursLayout.concat(rowOfMaps[whichMap_x].coloursLayout); // merge along x-axis
    }
  });
  for (let i = 0; i < maps[0][0].coloursLayout.length; i++) {
    // for each column in resulting map
    for (let j = 1; j < maps.length; j++) {
      // for each column needing to be merged into resulting map
      maps[0][0].coloursLayout[i] = maps[0][0].coloursLayout[i].concat(maps[j][0].coloursLayout[i]);
    }
  }
  // Merge materials counts for lookup
  let materials_new = {};
  Object.keys(coloursJSON).forEach((colourSetId) => {
    materials_new[colourSetId] = 0;
  });
  maps.forEach((rowOfMaps) => {
    rowOfMaps.forEach((map) => {
      Object.keys(map.materials).forEach((colourSetId) => {
        materials_new[colourSetId] += map.materials[colourSetId];
      });
    });
  });
  maps[0][0].materials = materials_new;
  maps = [[maps[0][0]]];
}

function setupColoursLayoutsFromPixelsData() {
  const mapSize_x = maps[0].length;
  const mapSize_z = maps.length;
  for (let whichMap_z = 0; whichMap_z < mapSize_z; whichMap_z++) {
    for (let whichMap_x = 0; whichMap_x < mapSize_x; whichMap_x++) {
      let coloursLayout: Colour[][] = [];
      for (let columnNumber = 0; columnNumber < 128; columnNumber++) {
        let coloursLayout_columnToPush: Colour[] = [];
        for (let rowNumber = 0; rowNumber < 128; rowNumber++) {
          const pixelsData_offset = 4 * (128 * mapSize_x * (128 * whichMap_z + rowNumber) + 128 * whichMap_x + columnNumber);
          const colourSetIdAndTone = exactRGBToColourSetIdAndTone([
            pixelsData[pixelsData_offset],
            pixelsData[pixelsData_offset + 1],
            pixelsData[pixelsData_offset + 2],
          ]);
          coloursLayout_columnToPush.push(colourSetIdAndTone);
        }
        coloursLayout.push(coloursLayout_columnToPush);
      }
      maps[whichMap_z][whichMap_x].coloursLayout = coloursLayout;
    }
  }
}



function reportProgress(progress: number) {
  postMessage({
    head: progressReportHead,
    body: progress,
  });
}

onmessage = async (e) => {
  coloursJSON = e.data.body.coloursJSON;
  MapModes = e.data.body.MapModes;
  WhereSupportBlocksModes = e.data.body.WhereSupportBlocksModes;
  optionValue_version = e.data.body.optionValue_version;
  optionValue_staircasing = e.data.body.optionValue_staircasing;
  optionValue_whereSupportBlocks = e.data.body.optionValue_whereSupportBlocks;
  optionValue_supportBlock = e.data.body.optionValue_supportBlock;
  pixelsData = e.data.body.pixelsData;
  maps = e.data.body.maps;
  currentSelectedBlocks = e.data.body.currentSelectedBlocks;

  const headerMessage = e.data.head;

  setupExactColourCache();
  setupColoursLayoutsFromPixelsData();

  if (["CREATE_NBT_JOINED_FOR_VIEW_ONLINE", "CREATE_NBT_JOINED"].includes(headerMessage)) {
    mergeMaps();
  }

  progressReportHead = `PROGRESS_REPORT_${headerMessage}`;

  switch (headerMessage) {
    case "CREATE_NBT_JOINED_FOR_VIEW_ONLINE":
    case "CREATE_NBT_JOINED":
    case "CREATE_NBT_SPLIT": {
      for (let whichMap_y = 0; whichMap_y < maps.length; whichMap_y++) {
        for (let whichMap_x = 0; whichMap_x < maps[0].length; whichMap_x++) {
          const map_NBT = new Map_NBT(maps[whichMap_y][whichMap_x]);
          const NBT_Array = await map_NBT.getNBT();
          postMessage({
            head: headerMessage === "CREATE_NBT_JOINED_FOR_VIEW_ONLINE" ? "NBT_FOR_VIEW_ONLINE" : "NBT_ARRAY",
            body: {
              whichMap_x: whichMap_x,
              whichMap_y: whichMap_y,
              NBT_Array: NBT_Array,
            },
          });
        }
      }
      break;
    }
    case "CREATE_MAPDAT_SPLIT":
    case "CREATE_MAPDAT_SPLIT_ZIP": {
      for (let whichMap_y = 0; whichMap_y < maps.length; whichMap_y++) {
        for (let whichMap_x = 0; whichMap_x < maps[0].length; whichMap_x++) {
          const map_Mapdat = new Map_Mapdat(maps[whichMap_y][whichMap_x]);
          const Mapdat_Bytes = map_Mapdat.getNBT();
          postMessage({
            head: headerMessage === "CREATE_MAPDAT_SPLIT_ZIP" ? "MAPDAT_BYTES_ZIP" : "MAPDAT_BYTES",
            body: {
              whichMap_x: whichMap_x,
              whichMap_y: whichMap_y,
              Mapdat_Bytes: Mapdat_Bytes,
            },
          });
        }
      }
      break;
    }
    default: {
      throw new Error("Unknown header message");
    }
  }
};