namespace QrDine.Api;

// Demo data: one desi restaurant, 6 tables, a real Pakistani menu.
public static class Seed
{
    public static void Run(AppDb db)
    {
        if (db.Restaurants.Any()) return;

        var r = new Restaurant { Slug = "desidhaba", Name = "Desi Dhaba", Currency = "Rs" };
        db.Restaurants.Add(r);
        db.SaveChanges();

        for (int i = 1; i <= 6; i++)
            db.Tables.Add(new DiningTable { RestaurantId = r.Id, Code = $"T{i}", Name = $"Table {i}" });
        db.SaveChanges();

        var cats = new[]
        {
            new MenuCategory { RestaurantId = r.Id, Name = "BBQ & Karahi", SortOrder = 1 },
            new MenuCategory { RestaurantId = r.Id, Name = "Rice & Biryani", SortOrder = 2 },
            new MenuCategory { RestaurantId = r.Id, Name = "Breads",         SortOrder = 3 },
            new MenuCategory { RestaurantId = r.Id, Name = "Drinks",         SortOrder = 4 },
        };
        db.Categories.AddRange(cats);
        db.SaveChanges();

        int bbq = cats[0].Id, rice = cats[1].Id, bread = cats[2].Id, drink = cats[3].Id;

        db.MenuItems.AddRange(
            new MenuItem { RestaurantId = r.Id, CategoryId = bbq, Name = "Chicken Karahi (Full)", NameUrdu = "چکن کڑاہی", Description = "Boneless chicken, tomato, green chilli", Price = 1600, Emoji = "🍗", PrepMinutes = 20 },
            new MenuItem { RestaurantId = r.Id, CategoryId = bbq, Name = "Seekh Kebab (4 pcs)", NameUrdu = "سیخ کباب", Description = "Charcoal grilled minced beef", Price = 700, Emoji = "🍢", PrepMinutes = 15 },
            new MenuItem { RestaurantId = r.Id, CategoryId = bbq, Name = "Chicken Tikka", NameUrdu = "چکن تکہ", Description = "Grilled quarter chicken", Price = 550, Emoji = "🍗", PrepMinutes = 18 },
            new MenuItem { RestaurantId = r.Id, CategoryId = bbq, Name = "Mutton Karahi (Half)", NameUrdu = "مٹن کڑاہی", Description = "Tender mutton, house spices", Price = 2200, Emoji = "🥘", PrepMinutes = 30 },

            new MenuItem { RestaurantId = r.Id, CategoryId = rice, Name = "Chicken Biryani", NameUrdu = "چکن بریانی", Description = "Sindhi style, spicy", Price = 450, Emoji = "🍚", PrepMinutes = 10 },
            new MenuItem { RestaurantId = r.Id, CategoryId = rice, Name = "Beef Pulao", NameUrdu = "بیف پلاؤ", Description = "Yakhni pulao with raita", Price = 500, Emoji = "🍛", PrepMinutes = 10 },

            new MenuItem { RestaurantId = r.Id, CategoryId = bread, Name = "Roghni Naan", NameUrdu = "روغنی نان", Description = "Sesame topped", Price = 90, Emoji = "🫓", PrepMinutes = 6 },
            new MenuItem { RestaurantId = r.Id, CategoryId = bread, Name = "Garlic Naan", NameUrdu = "گارلک نان", Description = "Fresh from tandoor", Price = 120, Emoji = "🧄", PrepMinutes = 6 },
            new MenuItem { RestaurantId = r.Id, CategoryId = bread, Name = "Tandoori Roti", NameUrdu = "تندوری روٹی", Description = "Whole wheat", Price = 40, Emoji = "🫓", PrepMinutes = 5 },

            new MenuItem { RestaurantId = r.Id, CategoryId = drink, Name = "Fresh Lime", NameUrdu = "فریش لائم", Description = "Sweet or salted", Price = 200, Emoji = "🍋", PrepMinutes = 4 },
            new MenuItem { RestaurantId = r.Id, CategoryId = drink, Name = "Lassi (Sweet)", NameUrdu = "لسی", Description = "Chilled yogurt drink", Price = 250, Emoji = "🥛", PrepMinutes = 4 },
            new MenuItem { RestaurantId = r.Id, CategoryId = drink, Name = "Soft Drink", NameUrdu = "کولڈ ڈرنک", Description = "Regular bottle", Price = 120, Emoji = "🥤", PrepMinutes = 2 }
        );
        db.SaveChanges();
    }
}
