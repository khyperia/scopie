use crate::Result;
use glutin::{self, event::VirtualKeyCode as Key};
use khygl::{render_text::TextRenderer, render_texture::TextureRenderer, Rect};
use std::{convert::TryInto, mem::replace};

pub struct TextInput {
    input_text: String,
    message: String,
    exec: bool,
    okay: bool,
}

impl TextInput {
    pub fn new() -> Self {
        Self {
            input_text: String::new(),
            message: String::new(),
            okay: true,
            exec: false,
        }
    }

    pub fn try_get_exec_cmd(&mut self) -> Option<String> {
        if self.exec {
            self.exec = false;
            let result = replace(&mut self.input_text, String::new());
            Some(result)
        } else {
            None
        }
    }

    pub fn set_exec_result(&mut self, message: String, okay: bool) {
        self.message = message;
        self.okay = okay;
    }

    pub fn key_down(&mut self, key: Key) {
        match key {
            Key::Back => {
                self.input_text.pop();
            }
            Key::Return => self.exec = true,
            _ => (),
        }
    }

    pub fn received_character(&mut self, ch: char) {
        if ch >= ' ' {
            self.input_text.push(ch);
        }
    }

    pub fn render(
        &mut self,
        texture_renderer: &TextureRenderer,
        text_renderer: &mut TextRenderer,
        screen_size: (usize, usize),
    ) -> Result<usize> {
        let input_pos_y = screen_size.1 as isize - text_renderer.spacing as isize - 1;
        let input_pos_y = input_pos_y.try_into().unwrap_or(0);
        let input_pos = (10, input_pos_y);
        text_renderer.render(
            texture_renderer,
            &self.input_text,
            [1.0, 1.0, 1.0, 1.0],
            input_pos,
            screen_size,
        )?;
        let error_pos_y = screen_size.1 as isize - 2 * text_renderer.spacing as isize - 1;
        let error_pos_y = error_pos_y.try_into().unwrap_or(0);
        let error_pos = (10, error_pos_y);
        text_renderer.render(
            texture_renderer,
            &self.message,
            [1.0, 1.0, 1.0, 1.0],
            error_pos,
            screen_size,
        )?;
        let command_color = if self.okay {
            [0.5, 0.5, 0.5, 1.0]
        } else {
            [1.0, 0.5, 0.5, 1.0]
        };
        texture_renderer.rect(
            Rect::new(
                input_pos.0,
                input_pos.1,
                (screen_size.0 as isize - input_pos.0 as isize * 2)
                    .try_into()
                    .unwrap_or(2),
                text_renderer.spacing,
            ),
            command_color,
            (screen_size.0 as f32, screen_size.1 as f32),
        )?;
        Ok(if self.message.is_empty() {
            input_pos_y
        } else {
            error_pos_y
        })
    }
}
