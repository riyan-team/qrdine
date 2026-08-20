namespace QrDine.Application.Admin;

/// <summary>
/// A ready-to-edit starter menu every new restaurant is seeded with, so owners
/// tweak prices/items instead of building the whole menu from an empty screen.
/// Tuned for a typical Pakistani restaurant (prices in Rs, indicative). Items with
/// Half/Full sizes use variants; everything is editable in Menu management.
/// </summary>
public static class StarterMenu
{
    public record Item(string Name, string Urdu, decimal Price, string Emoji, int Prep, (string Name, decimal Price)[]? Variants = null);
    public record Category(string Name, Item[] Items);

    public static readonly Category[] Categories =
    {
        new("Soups & Starters", new Item[]
        {
            new("Chicken Corn Soup", "چکن کارن سوپ", 250, "🍲", 10),
            new("Hot & Sour Soup", "ہاٹ اینڈ سار سوپ", 280, "🍲", 10),
            new("Spring Rolls (4 pcs)", "اسپرنگ رولز", 220, "🥟", 12),
            new("Chicken Samosa", "چکن سموسہ", 60, "🥟", 8),
            new("Chicken Pakora", "چکن پکوڑا", 300, "🍤", 12),
        }),
        new("Biryani", new Item[]
        {
            new("Chicken Biryani", "چکن بریانی", 350, "🍚", 20),
            new("Beef Biryani", "بیف بریانی", 400, "🍚", 20),
            new("Mutton Biryani", "مٹن بریانی", 550, "🍚", 25),
            new("Vegetable Biryani", "سبزی بریانی", 250, "🍚", 18),
        }),
        new("Pulao", new Item[]
        {
            new("Chicken Pulao", "چکن پلاؤ", 320, "🍚", 20),
            new("Beef Pulao", "بیف پلاؤ", 380, "🍚", 20),
            new("Matar Pulao", "مٹر پلاؤ", 220, "🍚", 18),
        }),
        new("Karahi & Handi", new Item[]
        {
            new("Chicken Karahi", "چکن کڑاہی", 1300, "🍗", 25, new[] { ("Half", 700m), ("Full", 1300m) }),
            new("Beef Karahi", "بیف کڑاہی", 1400, "🍗", 30, new[] { ("Half", 750m), ("Full", 1400m) }),
            new("Mutton Karahi", "مٹن کڑاہی", 2100, "🍗", 30, new[] { ("Half", 1100m), ("Full", 2100m) }),
            new("Chicken Handi", "چکن ہانڈی", 1200, "🍲", 25, new[] { ("Half", 650m), ("Full", 1200m) }),
            new("White Karahi", "وائٹ کڑاہی", 1500, "🍗", 28, new[] { ("Half", 800m), ("Full", 1500m) }),
        }),
        new("Salan / Curry", new Item[]
        {
            new("Aloo Gosht", "آلو گوشت", 380, "🍛", 20),
            new("Palak Gosht", "پالک گوشت", 420, "🍛", 22),
            new("Chicken Qorma", "چکن قورمہ", 400, "🍛", 20),
            new("Bhindi Gosht", "بھنڈی گوشت", 360, "🍛", 20),
            new("Daal Mash", "دال ماش", 250, "🥘", 15),
            new("Daal Chana", "دال چنا", 220, "🥘", 15),
            new("Mix Sabzi", "مکس سبزی", 240, "🥗", 15),
        }),
        new("Nihari & Haleem", new Item[]
        {
            new("Beef Nihari", "بیف نہاری", 450, "🍲", 20),
            new("Chicken Nihari", "چکن نہاری", 400, "🍲", 20),
            new("Haleem", "حلیم", 300, "🍲", 15),
        }),
        new("BBQ & Tikka", new Item[]
        {
            new("Chicken Tikka", "چکن تکہ", 320, "🍢", 18),
            new("Chicken Malai Boti", "چکن ملائی بوٹی", 380, "🍢", 18),
            new("Seekh Kabab (2 pcs)", "سیخ کباب", 280, "🍢", 15),
            new("Beef Boti", "بیف بوٹی", 400, "🍢", 18),
            new("Reshmi Kabab", "ریشمی کباب", 360, "🍢", 18),
        }),
        new("Fast Food", new Item[]
        {
            new("Zinger Burger", "زنگر برگر", 350, "🍔", 12),
            new("Chicken Shawarma", "چکن شوارما", 220, "🌯", 10),
            new("Club Sandwich", "کلب سینڈوچ", 320, "🥪", 12),
            new("French Fries", "فرنچ فرائز", 200, "🍟", 8),
        }),
        new("Naan & Roti", new Item[]
        {
            new("Roti", "روٹی", 25, "🫓", 5),
            new("Naan", "نان", 40, "🫓", 5),
            new("Roghni Naan", "روغنی نان", 70, "🫓", 6),
            new("Garlic Naan", "گارلک نان", 90, "🫓", 6),
            new("Kulcha", "کلچہ", 60, "🫓", 6),
        }),
        new("Rice", new Item[]
        {
            new("Plain Rice", "سادہ چاول", 180, "🍚", 10),
            new("Zeera Rice", "زیرہ چاول", 220, "🍚", 12),
        }),
        new("Raita & Salad", new Item[]
        {
            new("Raita", "رائتہ", 80, "🥗", 5),
            new("Green Salad", "سلاد", 100, "🥗", 5),
            new("Kachumber Salad", "کچومر سلاد", 120, "🥗", 5),
        }),
        new("Desserts", new Item[]
        {
            new("Kheer", "کھیر", 180, "🍮", 5),
            new("Gulab Jamun (2 pcs)", "گلاب جامن", 150, "🍥", 5),
            new("Zarda", "زردہ", 200, "🍚", 5),
            new("Ras Malai", "رس ملائی", 220, "🍥", 5),
            new("Kulfi", "قلفی", 150, "🍧", 5),
        }),
        new("Beverages", new Item[]
        {
            new("Soft Drink", "سافٹ ڈرنک", 80, "🥤", 3),
            new("Mineral Water", "منرل واٹر", 60, "💧", 2),
            new("Fresh Lime", "فریش لائم", 150, "🍋", 5),
            new("Sweet Lassi", "میٹھی لسی", 180, "🥛", 5),
            new("Doodh Patti Chai", "دودھ پتی چائے", 100, "☕", 6),
            new("Mint Margarita", "منٹ مارگریٹا", 250, "🍹", 5),
        }),
    };
}
