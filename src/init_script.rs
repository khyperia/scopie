use Result;
use std::collections::HashMap;
use std::fs::File;
use std::io::BufRead;
use std::io::BufReader;

pub struct InitScript {
    map: HashMap<String, Vec<String>>,
}

impl InitScript {
    pub fn new(path: &str) -> Result<InitScript> {
        let f = File::open(path)?;
        let file = BufReader::new(&f);
        let mut first = true;
        let mut cur = String::new();
        let mut map = HashMap::new();
        for line in file.lines() {
            let line = line?;
            if line.is_empty() {
                first = true;
            } else if first {
                first = false;
                cur = line;
            } else {
                let entry = map.entry(cur.clone()).or_insert_with(|| Vec::new());
                entry.push(line);
            }
        }
        Ok(InitScript { map })
    }

    pub fn script(&self, key: &str) -> Vec<String> {
        self.map.get(key).cloned().unwrap_or_else(|| Vec::new())
    }
}
