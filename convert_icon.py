from PIL import Image
import sys

def convert(input_path, output_png, output_ico):
    try:
        img = Image.open(input_path).convert("RGBA")
        
        # Make sure it's square
        size = min(img.size)
        
        # Crop to square at center
        left = (img.size[0] - size) // 2
        top = (img.size[1] - size) // 2
        right = (img.size[0] + size) // 2
        bottom = (img.size[1] + size) // 2
        
        square_img = img.crop((left, top, right, bottom))
        
        # Save as PNG
        square_img.save(output_png, format="PNG")
        print(f"Saved {output_png}")
        
        # Save as ICO (multiple sizes)
        icon_sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
        square_img.save(output_ico, format="ICO", sizes=icon_sizes)
        print(f"Saved {output_ico}")
        
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    convert(
        r"C:\Users\playe\.gemini\antigravity\brain\817875d4-ca6d-49e8-b2da-77079bc88a5b\media__1774472335574.png",
        r"E:\Projects\PCApps\FinalLauncher\Assets\icon.png",
        r"E:\Projects\PCApps\FinalLauncher\Assets\icon.ico"
    )
