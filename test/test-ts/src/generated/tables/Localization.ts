// ------------------------------------------------------------------------------
// <auto-generated>
//     THIS CODE WAS GENERATED BY SheetMan.
//
//     CHANGES TO THIS FILE MAY CAUSE INCORRECT BEHAVIOR AND WILL BE LOST IF
//     THE CODE IS REGENERATED.
// </auto-generated>
// ------------------------------------------------------------------------------

import * as fs from 'fs'

/** A type for handling rows when parsing .json. */
interface IDataRow {
    index: number
    key: string
    description: string
    english: string
    korean: string
    spanish: string
    chinese: string
    french: string
    german: string
    indonesian: string
    japanese: string
    portuguese: string
    russian: string
    vietnamese: string
}

// Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1887047427&range=B2
/** 로컬라이제이션 테이블입니다. */
export class LocalizationRecord {
    /** Default constructor */
    constructor() {
    }

    /** 인덱스(필수) */
    public get index(): number { return this._index }
    private _index: number

    /** 스트링 키 */
    public get key(): string { return this._key }
    private _key: string

    /** 설명 */
    public get description(): string { return this._description }
    private _description: string

    /** 영어 */
    public get english(): string { return this._english }
    private _english: string

    /** 한글 */
    public get korean(): string { return this._korean }
    private _korean: string

    /** 스페니시 */
    public get spanish(): string { return this._spanish }
    private _spanish: string

    /** 중국어 */
    public get chinese(): string { return this._chinese }
    private _chinese: string

    /** 프랑스어 */
    public get french(): string { return this._french }
    private _french: string

    /** 독일어 */
    public get german(): string { return this._german }
    private _german: string

    /** 인도네시아어 */
    public get indonesian(): string { return this._indonesian }
    private _indonesian: string

    /** 일본어 */
    public get japanese(): string { return this._japanese }
    private _japanese: string

    /** 포루투칼어 */
    public get portuguese(): string { return this._portuguese }
    private _portuguese: string

    /** 러시아어 */
    public get russian(): string { return this._russian }
    private _russian: string

    /** 베트남어 */
    public get vietnamese(): string { return this._vietnamese }
    private _vietnamese: string

    /** Populate field values. */
    public populateFieldValues(dataRow: IDataRow): void {
        this._index = dataRow.index
        this._key = dataRow.key
        this._description = dataRow.description
        this._english = dataRow.english
        this._korean = dataRow.korean
        this._spanish = dataRow.spanish
        this._chinese = dataRow.chinese
        this._french = dataRow.french
        this._german = dataRow.german
        this._indonesian = dataRow.indonesian
        this._japanese = dataRow.japanese
        this._portuguese = dataRow.portuguese
        this._russian = dataRow.russian
        this._vietnamese = dataRow.vietnamese
    }

    /** Populate field values. */
    public populateFieldValuesCompact(dataRow: any[]): void {
        let offset = 0
        this._index = dataRow[offset++]
        this._key = dataRow[offset++]
        this._description = dataRow[offset++]
        this._english = dataRow[offset++]
        this._korean = dataRow[offset++]
        this._spanish = dataRow[offset++]
        this._chinese = dataRow[offset++]
        this._french = dataRow[offset++]
        this._german = dataRow[offset++]
        this._indonesian = dataRow[offset++]
        this._japanese = dataRow[offset++]
        this._portuguese = dataRow[offset++]
        this._russian = dataRow[offset++]
        this._vietnamese = dataRow[offset++]
    }
}

// Generated from https://docs.google.com/spreadsheets/d/10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU/edit#gid=1887047427&range=B2
/** 로컬라이제이션 테이블입니다. */
export class LocalizationTable {
    /** Default constructor. */
    constructor() {
    }

    /** All records. */
    public get records(): LocalizationRecord[] { return this._records }
    private _records: LocalizationRecord[] = []

    // Indexing by 'index'
    public get recordsByIndex(): Map<number, LocalizationRecord> { return this._recordsByIndex }
    private _recordsByIndex: Map<number, LocalizationRecord> = new Map<number, LocalizationRecord>()

    /** Gets the value associated with the specified key. throw Error if not found. */
    public getByIndex(key: number): LocalizationRecord {
        const found = this._recordsByIndex.get(key)
        if (!found)
            throw new Error(`There is no record in table "Localization" that corresponds to field "index" value ${key}`)

        return found
    }

    /** Gets the value associated with the specified key. */
    public tryGetByIndex(key: number): LocalizationRecord | undefined {
        return this._recordsByIndex.get(key)
    }

    /** Determines whether the table contains the specified key. */
    public containsIndex(key: number): boolean {
        return !!this._recordsByIndex.has(key)
    }

    // Indexing by 'key'
    public get recordsByKey(): Map<string, LocalizationRecord> { return this._recordsByKey }
    private _recordsByKey: Map<string, LocalizationRecord> = new Map<string, LocalizationRecord>()

    /** Gets the value associated with the specified key. throw Error if not found. */
    public getByKey(key: string): LocalizationRecord {
        const found = this._recordsByKey.get(key)
        if (!found)
            throw new Error(`There is no record in table "Localization" that corresponds to field "key" value ${key}`)

        return found
    }

    /** Gets the value associated with the specified key. */
    public tryGetByKey(key: string): LocalizationRecord | undefined {
        return this._recordsByKey.get(key)
    }

    /** Determines whether the table contains the specified key. */
    public containsKey(key: string): boolean {
        return !!this._recordsByKey.has(key)
    }

    /** Read a table from specified file. */
    public async read(filename: string): Promise<void> {
        const json = await fs.promises.readFile(filename, "utf8")
        this.readFromJson(json)
    }

    /** Read a table from specified file synchronously. */
    public readSync(filename: string): void {
        const json = fs.readFileSync(filename, "utf8")
        this.readFromJson(json)
    }

    private readFromJson(json: string): void {
        const dataRows: any[] = JSON.parse(json)
        if (this.isCompactRowFormatted(dataRows)) {
            for (const dataRow of dataRows) {
                const record = new LocalizationRecord()
                record.populateFieldValuesCompact(dataRow)
                this._records.push(record)
            }
        } else {
            for (const dataRow of dataRows as IDataRow[]) {
                const record = new LocalizationRecord()
                record.populateFieldValues(dataRow)
                this._records.push(record)
            }
        }

        this.mapping()
    }

    private isCompactRowFormatted(rows: any[]): boolean {
        return rows.length > 0 && Array.isArray(rows[0])
    }

    /** Index mapping. */
    private mapping(): void {
        for (const record of this._records)
            this._recordsByIndex.set(record.index, record);
        for (const record of this._records)
            this._recordsByKey.set(record.key, record);
    }
}

