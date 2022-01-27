import * as path from 'path'
import { Tables } from './generated'
import { ValueType } from './generated'

//import * as fs from 'fs'
//import * as stream from 'stream'
//import axios from 'axios'
//import { promisify } from 'util'
//import os from 'os'

const tables = new Tables();

(async () => {
    await tables.readAll(path.join(__dirname, 'files'))

    const locTable = tables.localization
    console.log('recordcount: ' + locTable.records.length)

    for (let r of locTable.records) {
        console.log('ROW: ' + JSON.stringify(r))
        console.log("")
    }
  
    //console.log('__dirname: ' + __dirname)
    //console.log('ValueType: ' + ValueType.Bigint)

    const testFieldTypesTable = tables.testFieldTypes
    console.log(testFieldTypesTable)
    
  })()




/*
//await axios.get('https://s.zigbang.net/download/metapolis/tables/manifest.json')

(async () => {
    const finishedDownload = promisify(stream.finished);
    const writer = fs.createWriteStream('remote-manifest.json');
  
    const response = await axios({
      method: 'GET',
      url: 'https://s.zigbang.net/download/metapolis/tables/manifest.json',
      responseType: 'stream',
    });
  
    response.data.pipe(writer);
    await finishedDownload(writer);
  })();

  console.log(os.tmpdir())
*/
