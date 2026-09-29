# Class diagram generator

- `model.py`: classes, stereotypes and members
- `layout.py`: box positions and line routes
- `render.py`: draws the SVG and checks the diagram (undefined types, lines that don't match fields, lines through boxes or package titles)

```
pip install pillow
python3 render.py ../nsc-unity-class-diagram.svg
PW=$(npm root -g)/playwright node shot.js "$PWD/../nsc-unity-class-diagram.svg" "$PWD/../nsc-unity-class-diagram.png" 2
```
