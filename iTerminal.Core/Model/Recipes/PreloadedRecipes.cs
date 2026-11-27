namespace iTerminal.Core.Model.Recipes;

/// <summary>
/// The favourite recipes that ship with the application: two each from Switzerland, Germany, France, Italy and Hungary.
/// They are code, not rows, so they need no seeding and cannot be lost or overwritten by a change to the database. Their
/// text is English; only the titles keep the name the dish has in its own country. Each has a fixed identifier, so a
/// custom recipe can never share one.
/// </summary>
public static class PreloadedRecipes
{
    public static IReadOnlyList<Recipe> All { get; } =
    [
        Make("6a1d0001-5b1e-4c2a-9f10-000000000001", "Rösti", "Switzerland",
            ["1 kg waxy potatoes, boiled in their skins the day before and cooled", "60 g butter", "1 teaspoon salt", "Freshly ground pepper"],
            [
                "Peel the cold potatoes and grate them coarsely into a bowl.",
                "Season with salt and pepper and toss gently.",
                "Melt half the butter in a 24 cm non-stick pan over medium heat.",
                "Add the potatoes, press them flat and cook for about 10 minutes, until a crust forms underneath.",
                "Put a plate over the pan, turn the rösti out, add the rest of the butter to the pan and slide the rösti back to brown the other side for about 8 minutes.",
                "Serve at once, hot.",
            ]),
        Make("6a1d0002-5b1e-4c2a-9f10-000000000002", "Zürcher Geschnetzeltes", "Switzerland",
            ["600 g veal (fillet or shoulder), cut in thin strips", "1 tablespoon flour", "2 tablespoons butter", "1 onion, finely chopped", "200 g mushrooms, sliced", "1 dl dry white wine", "2 dl double cream", "1 teaspoon Dijon mustard", "Salt and pepper", "Fresh parsley, chopped", "Rösti to serve"],
            [
                "Season the veal strips and toss them lightly in the flour.",
                "Heat the butter in a large pan over high heat and brown the veal in two batches, 2 to 3 minutes each; set aside.",
                "Lower the heat, soften the onion in the same pan for 3 minutes, then add the mushrooms and cook until their liquid has evaporated.",
                "Pour in the wine and let it reduce by half, then stir in the cream and mustard.",
                "Return the veal and its juices to the pan and warm through for 2 minutes without boiling.",
                "Adjust the seasoning, sprinkle with parsley and serve with rösti.",
            ]),
        Make("6a1d0003-5b1e-4c2a-9f10-000000000003", "Sauerbraten", "Germany",
            ["1.5 kg beef topside or rump roast", "250 ml red wine vinegar", "250 ml dry red wine", "250 ml water", "1 onion, sliced", "1 carrot, sliced", "2 bay leaves", "6 cloves", "8 black peppercorns", "1 teaspoon salt", "3 tablespoons oil for searing", "1 onion, chopped, for the gravy", "2 tablespoons flour", "40 g gingerbread or gingersnaps, crumbed", "150 ml sour cream (optional)"],
            [
                "Bring the vinegar, wine, water, sliced onion and carrot, bay leaves, cloves, peppercorns and salt to the boil, then cool completely.",
                "Put the beef in a non-metal dish, pour the marinade over it, cover and refrigerate for 3 to 5 days, turning it once a day.",
                "Lift the meat out, pat it dry and season it; strain the marinade and keep it.",
                "Sear the meat in hot oil on all sides, then add the chopped onion and cook until golden.",
                "Pour in 500 ml of the strained marinade, cover and braise gently for 2 to 2.5 hours, until tender.",
                "Remove the meat and keep it warm. Stir the flour into the juices, simmer for 5 minutes, add the crumbed gingerbread and season; stir in the sour cream if using.",
                "Slice the meat, pour the sauce over it and serve with potato dumplings or red cabbage.",
            ]),
        Make("6a1d0004-5b1e-4c2a-9f10-000000000004", "Schwäbischer Kartoffelsalat", "Germany",
            ["1 kg waxy potatoes", "1 small onion, finely chopped", "150 ml hot beef stock", "3 tablespoons white wine vinegar", "4 tablespoons rapeseed oil", "1 teaspoon mustard", "1 teaspoon salt", "1 teaspoon sugar", "Pepper", "2 tablespoons chopped chives or parsley", "2 pickled gherkins, sliced (optional)"],
            [
                "Boil the potatoes in their skins until just tender, drain them, peel while warm and slice 5 mm thick.",
                "Whisk the vinegar, mustard, salt, sugar and pepper, add the hot stock and stir until the sugar has dissolved.",
                "Pour the dressing over the warm potatoes, add the onion, toss gently and let stand for 20 minutes.",
                "Add the oil and the herbs and toss again; add the gherkins if using.",
                "Serve warm or at room temperature.",
            ]),
        Make("6a1d0005-5b1e-4c2a-9f10-000000000005", "Coq au vin", "France",
            ["1.6 kg chicken, jointed", "150 g lardons or smoked bacon", "2 tablespoons butter or oil", "1 onion, sliced", "2 carrots, sliced", "3 garlic cloves, crushed", "75 cl red wine (Burgundy)", "300 ml chicken stock", "2 tablespoons brandy (optional)", "1 bouquet garni (thyme and bay)", "200 g small mushrooms", "12 pearl onions", "2 tablespoons flour", "30 g butter", "Salt and pepper", "Chopped parsley"],
            [
                "Marinate the chicken overnight in the wine with the onion, carrots, garlic and bouquet garni; drain, keep the wine and the vegetables, and pat the chicken dry.",
                "Fry the lardons until golden and remove them. Brown the chicken in the fat in batches and remove it.",
                "Soften the marinated vegetables in the same pot, sprinkle with the flour and cook for 1 minute.",
                "Pour in the marinade wine and the stock, return the chicken and lardons, add the bouquet garni, bring to a simmer, cover and cook for 1 hour.",
                "Meanwhile, brown the pearl onions and mushrooms in the butter.",
                "Add the brandy if using, then the onions and mushrooms, and simmer uncovered for 15 minutes until the sauce thickens. Season, sprinkle with parsley and serve with bread or potatoes.",
            ]),
        Make("6a1d0006-5b1e-4c2a-9f10-000000000006", "Soupe à l'oignon gratinée", "France",
            ["6 large onions, thinly sliced", "60 g butter", "1 teaspoon sugar", "1 tablespoon flour", "150 ml dry white wine", "1.5 litres beef stock", "1 bay leaf", "A sprig of thyme", "2 tablespoons Cognac (optional)", "Salt and pepper", "4 slices baguette", "150 g Gruyère, grated"],
            [
                "Melt the butter in a heavy pot over low heat, add the onions, the sugar and a pinch of salt, and cook slowly for 40 to 50 minutes, until deep golden.",
                "Sprinkle with the flour, stir for 2 minutes, then deglaze with the wine and let it reduce.",
                "Add the stock, bay leaf and thyme and simmer for 30 minutes. Remove the herbs, add the Cognac if using, and season.",
                "Toast the baguette slices. Ladle the soup into ovenproof bowls and top each with a slice of bread and grated cheese.",
                "Grill until the cheese melts and browns, and serve at once.",
            ]),
        Make("6a1d0007-5b1e-4c2a-9f10-000000000007", "Spaghetti alla carbonara", "Italy",
            ["400 g spaghetti", "150 g guanciale or pancetta, cut in strips", "4 egg yolks and 1 whole egg", "80 g Pecorino Romano, grated", "40 g Parmigiano Reggiano, grated", "Freshly ground black pepper", "Salt for the cooking water"],
            [
                "Bring a large pot of water to the boil and salt it generously.",
                "Fry the guanciale over medium heat until crisp and its fat has rendered; take the pan off the heat.",
                "Beat the egg yolks, the whole egg, both cheeses and plenty of pepper in a bowl.",
                "Cook the spaghetti until al dente, keeping a cup of the cooking water, and drain.",
                "Add the hot spaghetti to the guanciale pan, off the heat, and toss.",
                "Pour in the egg mixture and toss quickly, adding a splash of the cooking water until creamy. Serve at once with more pepper.",
            ]),
        Make("6a1d0008-5b1e-4c2a-9f10-000000000008", "Risotto alla milanese", "Italy",
            ["320 g Carnaroli or Arborio rice", "1 litre meat or chicken stock, kept hot", "1 onion, finely chopped", "80 g butter, divided", "100 ml dry white wine", "A generous pinch of saffron threads", "60 g Parmigiano Reggiano, grated", "Salt and pepper"],
            [
                "Soak the saffron in a ladle of the hot stock.",
                "Melt 40 g of the butter and soften the onion gently for 5 minutes, without browning it.",
                "Add the rice and stir for 2 minutes, until the grains are translucent at the edges.",
                "Add the wine and stir until it is absorbed.",
                "Add the hot stock one ladle at a time, stirring and letting each one be absorbed before the next, for 16 to 18 minutes, until the rice is creamy and still firm to the bite.",
                "Stir in the saffron stock with the last ladle. Take the pan off the heat, beat in the remaining butter and the Parmesan, rest for 2 minutes, season and serve.",
            ]),
        Make("6a1d0009-5b1e-4c2a-9f10-000000000009", "Gulyás", "Hungary",
            ["800 g beef chuck, cubed", "2 tablespoons lard or oil", "3 onions, chopped", "2 garlic cloves, crushed", "2 tablespoons sweet Hungarian paprika", "1 teaspoon caraway seeds", "1 teaspoon marjoram", "2 bell peppers, sliced", "2 tomatoes, chopped", "1.5 litres water", "2 medium potatoes, cubed", "Salt"],
            [
                "Fry the onions in the fat over medium heat for 10 minutes, until golden and soft.",
                "Take the pan off the heat and stir in the paprika quickly so it does not burn, then add the beef.",
                "Return the pan to low heat, add the garlic, caraway and marjoram and brown the meat for 10 minutes.",
                "Add the peppers and tomatoes, cover and sweat for 10 minutes.",
                "Pour in the water, bring to the boil, then simmer covered for 1 to 1.5 hours, until the beef is tender.",
                "Add the potatoes and simmer for 20 minutes more. Season with salt and serve hot, with bread.",
            ]),
        Make("6a1d0010-5b1e-4c2a-9f10-000000000010", "Csirkepaprikás", "Hungary",
            ["1 chicken (about 1.5 kg), cut in 8 pieces", "2 tablespoons lard", "2 onions, finely chopped", "2 tablespoons sweet paprika", "1 green pepper, sliced", "2 tomatoes, chopped", "250 ml chicken stock", "200 ml sour cream", "1 tablespoon flour", "Salt", "A pinch of caraway (optional)"],
            [
                "Season the chicken with salt and fry it in the lard until golden on all sides; remove it.",
                "Fry the onions in the same fat until soft and golden, take the pan off the heat and stir in the paprika.",
                "Return the chicken to the pan, add the pepper, tomatoes and stock, cover and simmer for 40 minutes, until tender.",
                "Mix the sour cream with the flour and a ladle of the sauce, then stir it back into the pot.",
                "Simmer for 5 minutes without boiling hard. Season and serve with nokedli or egg noodles.",
            ]),
    ];

    /// <summary>True when <paramref name="id"/> is one of the preloaded recipes.</summary>
    public static bool IsPreloaded(Guid id) => All.Any(recipe => recipe.Id == id);

    private static Recipe Make(string id, string title, string origin, string[] ingredients, string[] steps) =>
        new(Guid.Parse(id), title, origin, ingredients, steps, IsPreloaded: true);
}
