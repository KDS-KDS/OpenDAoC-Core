using DOL.GS.PlayerClass;

namespace DOL.GS.Scripts
{
    //public class MimicChampion : MimicNPC
    //{
    //    public MimicChampion(byte level) : base(new ClassChampion(), level)
    //    { }
    //}

    public class ChampionSpec : MimicSpec
    {
        public ChampionSpec(eSpecType spec)
        {
            SpecName = "ChampionSpec";

            int randBaseWeap = Util.Random(2);

            switch (randBaseWeap)
            {
                case 0: WeaponOneType = eObjectType.Blades; break;
                case 1: WeaponOneType = eObjectType.Piercing; break;
                case 2: WeaponOneType = eObjectType.Blunt; break;
            }

            WeaponTwoType = eObjectType.LargeWeapons;

            var randVariance = spec switch
            {
                eSpecType.OneHanded => Util.Random(0, 1),
                eSpecType.OneHandAndShield => Util.Random(0, 1),
                eSpecType.TwoHanded => 2,
                _ => Util.Random(3),
            };

            switch (randVariance)
            {
                case 0:
                SpecType = eSpecType.OneHandAndShield;
                Add(ObjToSpec(WeaponOneType), 39, 0.8f);
                Add(Specs.Valor, 50, 1.0f);
                Add(Specs.Shields, 42, 0.5f);
                Add(Specs.Parry, 6, 0.1f);
                break;

                case 1:
                SpecType = eSpecType.OneHandAndShield;
                Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                Add(Specs.Valor, 50, 1.0f);
                Add(Specs.Shields, 28, 0.5f);
                Add(Specs.Parry, 6, 0.1f);
                break;

                case 2:
                case 3:
                Is2H = true;
                SpecType = eSpecType.TwoHanded;
                Add(ObjToSpec(WeaponTwoType), 50, 0.9f);
                Add(Specs.Valor, 50, 1.0f);
                Add(Specs.Parry, 28, 0.1f);
                break;
            }
        }
    }
}